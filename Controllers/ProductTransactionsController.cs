using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

/// <summary>
/// Manages manual product movements and exposes sales-order lines as read-only
/// product history. Sales orders already own their stock movements in the
/// DbContext, so projecting them here avoids debiting stock twice.
/// </summary>
[Route("api/products/{productId:guid}/transactions")]
public sealed class ProductTransactionsController(KanchimeshDbContext database) : ApiControllerBase
{
    private static readonly IReadOnlyList<string> TransactionTypes =
        ["Sale", "Purchase", "Return", "Adjustment"];

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductTransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ProductTransactionDto>>> GetTransactions(
        Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, tracking: false, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        (page, pageSize) = NormalizePage(page, pageSize);
        var history = await GetHistoryAsync(product, cancellationToken);
        var orderedHistory = history
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.CreatedAtUtc)
            .ToList();

        return Ok(new PagedResult<ProductTransactionDto>(
            orderedHistory.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            page,
            pageSize,
            orderedHistory.Count));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(ProductTransactionsSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionsSummaryDto>> GetSummary(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, tracking: false, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var history = await GetHistoryAsync(product, cancellationToken);
        var sales = history.Where(transaction => IsType(transaction.TransactionType, "Sale")).ToList();
        var purchases = history.Where(transaction => IsType(transaction.TransactionType, "Purchase")).ToList();

        return Ok(new ProductTransactionsSummaryDto(
            product.Id,
            product.Name,
            product.ProductCode,
            product.Unit,
            sales.Sum(transaction => transaction.Quantity),
            purchases.Sum(transaction => transaction.Quantity),
            sales.Sum(transaction => transaction.Amount),
            purchases.Sum(transaction => transaction.Amount),
            history.Count));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionDto>> GetTransaction(
        Guid productId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, tracking: false, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var orderSale = await GetOrderSaleAsync(product, id, cancellationToken);
        if (orderSale is not null)
        {
            return Ok(orderSale);
        }

        var transaction = await database.ProductTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.ProductId == productId, cancellationToken);
        return transaction is null ? NotFound() : Ok(transaction.ToDto(product));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionDto>> CreateTransaction(
        Guid productId,
        ProductTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, tracking: true, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (!WorkflowValues.TryNormalize(request.TransactionType, TransactionTypes, out var transactionType))
        {
            return ValidationError(nameof(request.TransactionType), $"Transaction type must be one of: {string.Join(", ", TransactionTypes)}.");
        }

        var requestError = ValidateRequest(request);
        if (requestError is not null)
        {
            return ValidationError(requestError.Value.Field, requestError.Value.Message);
        }

        var transaction = CreateTransactionEntity(productId, request, transactionType);
        if (!CanApplyStockEffect(product, GetStockEffect(transaction)))
        {
            return InsufficientStockError(product, nameof(request.Quantity));
        }

        ApplyStockEffect(product, GetStockEffect(transaction));
        database.ProductTransactions.Add(transaction);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetTransaction), new { productId, id = transaction.Id }, transaction.ToDto(product));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionDto>> UpdateTransaction(
        Guid productId,
        Guid id,
        ProductTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, tracking: true, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (await IsOrderSaleAsync(productId, id, cancellationToken))
        {
            return OrderSaleReadOnlyConflict();
        }

        var transaction = await database.ProductTransactions
            .SingleOrDefaultAsync(item => item.Id == id && item.ProductId == productId, cancellationToken);
        if (transaction is null)
        {
            return NotFound();
        }

        if (!WorkflowValues.TryNormalize(request.TransactionType, TransactionTypes, out var transactionType))
        {
            return ValidationError(nameof(request.TransactionType), $"Transaction type must be one of: {string.Join(", ", TransactionTypes)}.");
        }

        var requestError = ValidateRequest(request);
        if (requestError is not null)
        {
            return ValidationError(requestError.Value.Field, requestError.Value.Message);
        }

        var replacement = CreateTransactionEntity(productId, request, transactionType);
        var netEffect = GetStockEffect(replacement) - GetStockEffect(transaction);
        if (!CanApplyStockEffect(product, netEffect))
        {
            return InsufficientStockError(product, nameof(request.Quantity));
        }

        ApplyStockEffect(product, -GetStockEffect(transaction));
        ApplyRequest(transaction, request, transactionType);
        ApplyStockEffect(product, GetStockEffect(transaction));

        await database.SaveChangesAsync(cancellationToken);
        return Ok(transaction.ToDto(product));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTransaction(
        Guid productId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var product = await GetProductAsync(productId, tracking: true, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (await IsOrderSaleAsync(productId, id, cancellationToken))
        {
            return OrderSaleReadOnlyConflict();
        }

        var transaction = await database.ProductTransactions
            .SingleOrDefaultAsync(item => item.Id == id && item.ProductId == productId, cancellationToken);
        if (transaction is null)
        {
            return NotFound();
        }

        var revertEffect = -GetStockEffect(transaction);
        if (!CanApplyStockEffect(product, revertEffect))
        {
            return InsufficientStockError(product, nameof(ProductTransaction.Quantity));
        }

        ApplyStockEffect(product, revertEffect);
        database.ProductTransactions.Remove(transaction);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<Product?> GetProductAsync(Guid productId, bool tracking, CancellationToken cancellationToken)
    {
        var query = database.Products.Where(product => product.Id == productId);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<List<ProductTransactionDto>> GetHistoryAsync(Product product, CancellationToken cancellationToken)
    {
        var manualTransactions = await database.ProductTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ProductId == product.Id)
            .ToListAsync(cancellationToken);
        var salesOrderItems = await ActiveOrderItemsQuery(product.Id)
            .ToListAsync(cancellationToken);

        var history = manualTransactions.Select(transaction => transaction.ToDto(product)).ToList();
        history.AddRange(salesOrderItems.Select(item => ToOrderSaleDto(item, product)));
        return history;
    }

    private async Task<ProductTransactionDto?> GetOrderSaleAsync(
        Product product,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        var item = await ActiveOrderItemsQuery(product.Id)
            .SingleOrDefaultAsync(orderItem => orderItem.Id == itemId, cancellationToken);
        return item is null ? null : ToOrderSaleDto(item, product);
    }

    private Task<bool> IsOrderSaleAsync(Guid productId, Guid itemId, CancellationToken cancellationToken) =>
        database.SalesOrderItems
            .AsNoTracking()
            .AnyAsync(
                item => item.Id == itemId && item.ProductId == productId && item.SalesOrder.Status != "Cancelled",
                cancellationToken);

    private IQueryable<SalesOrderItem> ActiveOrderItemsQuery(Guid productId) =>
        database.SalesOrderItems
            .AsNoTracking()
            .Where(item => item.ProductId == productId && item.SalesOrder.Status != "Cancelled")
            .Include(item => item.SalesOrder)
                .ThenInclude(order => order.Customer)
            .Include(item => item.SalesOrder)
                .ThenInclude(order => order.Payments)
            .AsSplitQuery();

    private static ProductTransactionDto ToOrderSaleDto(SalesOrderItem item, Product product)
    {
        var order = item.SalesOrder;
        var customer = order.Customer;
        var paidAmount = order.Payments.Where(payment => !payment.IsAdvance).Sum(payment => payment.Amount);
        var paymentStatus = order.GrandTotal <= 0m || paidAmount >= order.GrandTotal
            ? "Paid"
            : paidAmount > 0m
                ? "Partial"
                : "Pending";
        var notes = string.IsNullOrWhiteSpace(order.Notes)
            ? $"Sales order {order.OrderNumber}"
            : $"Sales order {order.OrderNumber}. {order.Notes.Trim()}";

        return new ProductTransactionDto(
            item.Id,
            order.OrderNumber,
            product.Id,
            product.Name,
            product.ProductCode,
            "Sale",
            DtoMappings.DisplayCustomerName(customer),
            customer.Phone,
            customer.City ?? customer.Address,
            order.OrderDate,
            item.Quantity,
            product.Unit,
            item.LineTotal,
            paymentStatus,
            notes,
            order.CreatedAtUtc,
            order.UpdatedAtUtc,
            IsOrderSale: true,
            SourceOrderId: order.Id);
    }

    private static ProductTransaction CreateTransactionEntity(
        Guid productId,
        ProductTransactionRequest request,
        string transactionType)
    {
        var prefix = transactionType switch
        {
            "Sale" => "PS",
            "Purchase" => "PP",
            "Return" => "PR",
            "Adjustment" => "PA",
            _ => "PT",
        };
        var transaction = new ProductTransaction
        {
            TransactionNumber = DocumentNumbers.New(prefix),
            ProductId = productId,
        };
        ApplyRequest(transaction, request, transactionType);
        return transaction;
    }

    private static void ApplyRequest(
        ProductTransaction transaction,
        ProductTransactionRequest request,
        string transactionType)
    {
        transaction.TransactionType = transactionType;
        transaction.PartyName = NullOrTrim(request.PartyName);
        transaction.PartyMobile = NullOrTrim(request.PartyMobile);
        transaction.PartyLocation = NullOrTrim(request.PartyLocation);
        transaction.TransactionDate = request.TransactionDate;
        transaction.Quantity = request.Quantity;
        transaction.Amount = request.Amount;
        transaction.PaymentStatus = NullOrTrim(request.PaymentStatus) ?? "Paid";
        transaction.Notes = NullOrTrim(request.Notes);
    }

    private static (string Field, string Message)? ValidateRequest(ProductTransactionRequest request)
    {
        const decimal maximumQuantity = 999_999_999_999_999m;
        const decimal maximumAmount = 9_999_999_999_999_999.99m;
        if (request.Quantity <= 0m || request.Quantity > maximumQuantity || !FitsScale(request.Quantity, 0.001m))
        {
            return (nameof(request.Quantity), "Quantity must be greater than zero, within the supported range, and have at most 3 decimal places.");
        }

        if (request.Amount < 0m || request.Amount > maximumAmount || !FitsScale(request.Amount, 0.01m))
        {
            return (nameof(request.Amount), "Amount must be zero or greater, within the supported range, and have at most 2 decimal places.");
        }

        if (request.TransactionDate == DateOnly.MinValue)
        {
            return (nameof(request.TransactionDate), "Transaction date is required.");
        }

        return null;
    }

    private static StockEffect GetStockEffect(ProductTransaction transaction) =>
        transaction.TransactionType switch
        {
            "Sale" => new StockEffect(-transaction.Quantity, 0m, transaction.Quantity),
            "Purchase" => new StockEffect(transaction.Quantity, transaction.Quantity, 0m),
            "Return" => new StockEffect(transaction.Quantity, 0m, 0m),
            "Adjustment" => new StockEffect(transaction.Quantity, 0m, 0m),
            _ => StockEffect.None,
        };

    private static bool CanApplyStockEffect(Product product, StockEffect effect) =>
        product.QuantityOnHand + effect.QuantityOnHand >= 0m &&
        product.TotalStockAdded + effect.TotalStockAdded >= 0m &&
        product.TotalSold + effect.TotalSold >= 0m;

    private static void ApplyStockEffect(Product product, StockEffect effect)
    {
        product.QuantityOnHand += effect.QuantityOnHand;
        product.TotalStockAdded += effect.TotalStockAdded;
        product.TotalSold += effect.TotalSold;
    }

    private ActionResult InsufficientStockError(Product product, string field) =>
        ValidationError(
            field,
            $"This change would reduce {product.Name} below zero. Available stock is {product.QuantityOnHand:0.###} {product.Unit}.");

    private ConflictObjectResult OrderSaleReadOnlyConflict() => Conflict(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Sales-order history is read-only.",
        Detail = "Edit, cancel, or delete the source sales order to change this stock movement.",
    });

    private static bool IsType(string? actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private static bool FitsScale(decimal value, decimal smallestUnit) =>
        value % smallestUnit == 0m;

    private static string? NullOrTrim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct StockEffect(
        decimal QuantityOnHand,
        decimal TotalStockAdded,
        decimal TotalSold)
    {
        public static StockEffect None => new(0m, 0m, 0m);

        public static StockEffect operator -(StockEffect value) =>
            new(-value.QuantityOnHand, -value.TotalStockAdded, -value.TotalSold);

        public static StockEffect operator -(StockEffect left, StockEffect right) =>
            new(
                left.QuantityOnHand - right.QuantityOnHand,
                left.TotalStockAdded - right.TotalStockAdded,
                left.TotalSold - right.TotalSold);
    }
}

/// <summary>Extension method so the mapping stays co-located with the controller.</summary>
internal static class ProductTransactionExtensions
{
    internal static ProductTransactionDto ToDto(this ProductTransaction transaction, Product product) => new(
        transaction.Id,
        transaction.TransactionNumber,
        transaction.ProductId,
        product.Name,
        product.ProductCode,
        transaction.TransactionType,
        transaction.PartyName,
        transaction.PartyMobile,
        transaction.PartyLocation,
        transaction.TransactionDate,
        transaction.Quantity,
        product.Unit,
        transaction.Amount,
        transaction.PaymentStatus,
        transaction.Notes,
        transaction.CreatedAtUtc,
        transaction.UpdatedAtUtc);
}
