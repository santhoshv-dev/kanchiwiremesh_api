using System.Data;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/orders")]
public sealed class OrdersController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] Guid? customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.SalesOrders.AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.Payments)
            .Include(order => order.Items)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusTerm = status.Trim().ToLower();
            query = query.Where(order => order.Status.ToLower() == statusTerm);
        }

        if (customerId.HasValue)
        {
            query = query.Where(order => order.CustomerId == customerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(order =>
                order.OrderNumber.ToLower().Contains(term) ||
                order.Customer.ContactName.ToLower().Contains(term) ||
                (order.Customer.CompanyName ?? string.Empty).ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query
            .OrderByDescending(order => order.OrderDate)
            .ThenByDescending(order => order.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResult<OrderSummaryDto>(orders.Select(ToSummaryDto).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await GetOrderGraph(id, tracking: false, cancellationToken);
        return order is null ? NotFound() : Ok(ToDetailDto(order));
    }

    // Invoice screens receive the same tax-inclusive order payload plus the
    // current shared company profile, so address and payment instructions stay
    // consistent wherever an invoice is generated.
    [HttpGet("{id:guid}/invoice")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> GetInvoiceData(Guid id, CancellationToken cancellationToken)
    {
        var order = await GetOrderGraph(id, tracking: false, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var company = await database.CompanyProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == CompanyProfile.DefaultId, cancellationToken);
        return Ok(ToDetailDto(order, CompanySettingsController.ToDto(company)));
    }

    [HttpPost]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetailDto>> CreateOrder(OrderRequest request, CancellationToken cancellationToken)
    {
        var compatibilityError = await ResolveCompatibilityFields(request, cancellationToken);
        if (compatibilityError is not null)
        {
            return ValidationError(compatibilityError.Value.Field, compatibilityError.Value.Message);
        }

        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.OrderStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.OrderStatuses)}.");
        }

        var relationError = await ValidateRelations(request, cancellationToken);
        if (relationError is not null)
        {
            return ValidationError(relationError.Value.Field, relationError.Value.Message);
        }

        var totalError = ValidateTotals(request);
        if (totalError is not null)
        {
            return ValidationError(totalError.Value.Field, totalError.Value.Message);
        }

        var orderId = Guid.NewGuid();
        try
        {
            if (database.Database.IsRelational())
            {
                // SQL Server retry support is configured on the context. Keep the
                // whole serializable allocation transaction inside its execution
                // strategy and verify a possibly successful commit before retrying.
                await ExecutionStrategyExtensions.ExecuteInTransactionAsync<Guid, Guid>(
                    database.Database.CreateExecutionStrategy(),
                    orderId,
                    async (id, retryCancellationToken) =>
                    {
                        // A retry reuses this DbContext, so discard entities left
                        // over from an interrupted attempt before trying again.
                        database.ChangeTracker.Clear();
                        await SaveNewOrder(id, request, status, retryCancellationToken);
                        return id;
                    },
                    (id, retryCancellationToken) => database.SalesOrders
                        .AsNoTracking()
                        .AnyAsync(order => order.Id == id, retryCancellationToken),
                    (context, retryCancellationToken) => context.Database
                        .BeginTransactionAsync(IsolationLevel.Serializable, retryCancellationToken),
                    cancellationToken);
            }
            else
            {
                await SaveNewOrder(orderId, request, status, cancellationToken);
            }
        }
        catch (OrderNumberExhaustedException)
        {
            return OrderNumberExhaustedConflict();
        }
        catch (InsufficientStockException exception)
        {
            return ValidationError(nameof(request.Items), exception.Message);
        }

        var order = await GetOrderGraph(orderId, tracking: false, cancellationToken);
        if (order is null)
        {
            throw new InvalidOperationException("The newly created order could not be reloaded.");
        }

        return CreatedAtAction(nameof(GetOrder), new { order.Id }, ToDetailDto(order));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> UpdateOrder(
        Guid id,
        OrderRequest request,
        CancellationToken cancellationToken)
    {
        var compatibilityError = await ResolveCompatibilityFields(request, cancellationToken);
        if (compatibilityError is not null)
        {
            return ValidationError(compatibilityError.Value.Field, compatibilityError.Value.Message);
        }

        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.OrderStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.OrderStatuses)}.");
        }

        var relationError = await ValidateRelations(request, cancellationToken);
        if (relationError is not null)
        {
            return ValidationError(relationError.Value.Field, relationError.Value.Message);
        }

        var totalError = ValidateTotals(request);
        if (totalError is not null)
        {
            return ValidationError(totalError.Value.Field, totalError.Value.Message);
        }

        var order = await GetOrderGraph(id, tracking: true, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!string.Equals(
                GetFinancialYear(order.OrderDate),
                GetFinancialYear(request.OrderDate),
                StringComparison.Ordinal))
        {
            return ValidationError(
                nameof(request.OrderDate),
                "Order date cannot be moved to a different financial year after its invoice number has been issued.");
        }

        if (order.Payments.Count > 0 && order.CustomerId != request.CustomerId)
        {
            return ValidationError(nameof(request.CustomerId), "The customer cannot be changed after payments have been recorded for an order.");
        }

        if (CannotCancel(status, order))
        {
            return PaidOrderCancellationConflict();
        }

        if (!IsCancelled(status))
        {
            var currentAllocation = IsCancelled(order.Status)
                ? EmptyProductQuantities
                : GetProductQuantities(order.Items);
            var stockError = await GetStockAvailabilityErrorAsync(
                GetProductQuantities(request.Items),
                currentAllocation,
                cancellationToken);
            if (stockError is not null)
            {
                return ValidationError(nameof(request.Items), stockError);
            }
        }

        ReplaceOrderValues(order, request, status);
        var appliedPaymentTotal = order.Payments.Where(payment => !payment.IsAdvance).Sum(payment => payment.Amount);
        if (order.GrandTotal < appliedPaymentTotal)
        {
            return ValidationError(nameof(request.Items), "The updated total cannot be less than payments already applied to this order.");
        }

        await OrderCalculator.SyncOrderCompletionAsync(database, order, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(order).Reference(item => item.Customer).LoadAsync(cancellationToken);
        return Ok(ToDetailDto(order));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(
        Guid id,
        OrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.OrderStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.OrderStatuses)}.");
        }

        var order = await GetOrderGraph(id, tracking: true, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (CannotCancel(status, order))
        {
            return PaidOrderCancellationConflict();
        }

        if (IsCancelled(order.Status) && !IsCancelled(status))
        {
            var stockError = await GetStockAvailabilityErrorAsync(
                GetProductQuantities(order.Items),
                EmptyProductQuantities,
                cancellationToken);
            if (stockError is not null)
            {
                return ValidationError(nameof(request.Status), stockError);
            }
        }

        order.Status = status;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToDetailDto(order));
    }

    [HttpPatch("{id:guid}/invoice-number")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> UpdateInvoiceNumber(
        Guid id,
        OrderInvoiceNumberRequest request,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderGraph(id, tracking: true, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.InvoiceNumber))
        {
            order.OrderNumber = request.InvoiceNumber.Trim();
            await database.SaveChangesAsync(cancellationToken);
        }

        return Ok(ToDetailDto(order));
    }


    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await GetOrderGraph(id, tracking: true, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        // Payments remain customer receipts when their optional source order
        // is removed.  This makes deletion safe for outstanding/received
        // figures while the DbContext restores the stock consumed by active
        // order lines in the same SaveChanges operation.
        foreach (var payment in order.Payments)
        {
            payment.SalesOrderId = null;
        }

        database.SalesOrders.Remove(order);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(string Field, string Message)?> ValidateRelations(OrderRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedDeliveryDate.HasValue && request.ExpectedDeliveryDate.Value < request.OrderDate)
        {
            return (nameof(request.ExpectedDeliveryDate), "Expected delivery date cannot be before the order date.");
        }

        if (!await database.Customers.AnyAsync(customer => customer.Id == request.CustomerId && customer.IsActive, cancellationToken))
        {
            return (nameof(request.CustomerId), "The selected active customer does not exist.");
        }

        if (request.Items.Count == 0)
        {
            return (nameof(request.Items), "At least one order item is required.");
        }

        var productIds = request.Items
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToList();
        if (productIds.Count > 0)
        {
            var matchedCount = await database.Products.CountAsync(
                product => productIds.Contains(product.Id) && product.IsActive,
                cancellationToken);
            if (matchedCount != productIds.Count)
            {
                return (nameof(request.Items), "Every selected product must exist and be active.");
            }
        }

        return null;
    }

    private async Task<(string Field, string Message)?> ResolveCompatibilityFields(OrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Date.HasValue)
        {
            request.OrderDate = DateOnly.FromDateTime(request.Date.Value);
        }

        if (request.CustomerId == Guid.Empty)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerName))
            {
                return (nameof(request.CustomerId), "customerId is required. The Flutter wireframe may supply customerName instead.");
            }

            var customerName = request.CustomerName.Trim().ToLower();
            var customerId = await database.Customers.AsNoTracking()
                .Where(customer => customer.IsActive &&
                    (customer.ContactName.ToLower() == customerName ||
                     (customer.CompanyName ?? string.Empty).ToLower() == customerName))
                .OrderBy(customer => customer.CreatedAtUtc)
                .Select(customer => customer.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (customerId == Guid.Empty)
            {
                return (nameof(request.CustomerName), "No active customer matches the supplied customer name.");
            }

            request.CustomerId = customerId;
        }

        if (request.Items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(request.ProductName) || !request.Amount.HasValue)
            {
                return (nameof(request.Items), "items are required. The Flutter wireframe may supply productName and amount instead.");
            }

            var productName = request.ProductName.Trim();
            var productId = await database.Products.AsNoTracking()
                .Where(product => product.IsActive && product.Name.ToLower() == productName.ToLower())
                .Select(product => (Guid?)product.Id)
                .FirstOrDefaultAsync(cancellationToken);
            request.Items =
            [
                new OrderItemRequest
                {
                    ProductId = productId,
                    Description = productName,
                    HsnSac = null,
                    Quantity = 1m,
                    Unit = "pcs",
                    Rate = request.Amount.Value,
                    IgstRate = 0m, SgstRate = 0m, CgstRate = 0m
                }
            ];
        }

        return null;
    }

    private static (string Field, string Message)? ValidateTotals(OrderRequest request)
    {
        // SalesOrder.Subtotal uses decimal(18,3), and values are rounded to
        // cents by OrderCalculator. Bound the subtotal before multiplying so a
        // model-valid quantity/rate pair cannot overflow System.Decimal.
        const decimal maximumSubtotal = 999_999_999_999_999.99m;
        var subtotal = 0m;
        foreach (var item in request.Items)
        {
            if (item.Rate >= 1m && item.Quantity > maximumSubtotal / item.Rate)
            {
                return (nameof(request.Items), "An order item's quantity and rate produce a subtotal that is too large.");
            }

            // For rates below one, Quantity is already limited to less than
            // maximumSubtotal, so this multiplication is also safe.
            var lineSubtotal = Math.Round(
                item.Quantity * item.Rate,
                2,
                MidpointRounding.AwayFromZero);
            if (lineSubtotal > maximumSubtotal - subtotal)
            {
                return (nameof(request.Items), "The order subtotal is too large.");
            }

            subtotal += lineSubtotal;
        }

        try
        {
            var calculatedOrder = new SalesOrder
            {
                DiscountAmount = request.DiscountAmount,
                FreightAmount = request.FreightAmount,
                Items = request.Items.Select(item => new SalesOrderItem
                {
                    Quantity = item.Quantity,
                    Rate = item.Rate,
                    IgstRate = item.IgstRate,
                    SgstRate = item.SgstRate,
                    CgstRate = item.CgstRate,
                }).ToList(),
            };
            OrderCalculator.Recalculate(calculatedOrder);
            const decimal maximumAmount = 9_999_999_999_999_999.99m;
            if (calculatedOrder.Items.Any(item =>
                    item.LineSubtotal > maximumAmount ||
                    item.TaxAmount > maximumAmount ||
                    item.LineTotal > maximumAmount) ||
                calculatedOrder.TaxAmount > maximumAmount ||
                calculatedOrder.GrandTotal > maximumAmount)
            {
                return (nameof(request.Items), "The calculated order total is too large.");
            }
        }
        catch (OverflowException)
        {
            return (nameof(request.Items), "The calculated order total is too large.");
        }

        return null;
    }

    private void ReplaceOrderValues(SalesOrder order, OrderRequest request, string status)
    {
        database.SalesOrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        foreach (var item in request.Items)
        {
            var replacement = new SalesOrderItem
            {
                SalesOrderId = order.Id,
                ProductId = item.ProductId,
                Description = item.Description.Trim(),
                HsnSac = Null(item.HsnSac),
                Specification = Null(item.Specification),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                Rate = item.Rate,
                IgstRate = item.IgstRate, SgstRate = item.SgstRate, CgstRate = item.CgstRate
            };

            // Adding an untracked child only through a navigation collection on
            // an existing order makes EF treat its generated GUID as an update
            // in some providers. Track it explicitly as Added so changing an
            // order quantity (for example 20 to 30) reliably replaces the old
            // line and lets the stock adjustment code apply the net delta.
            database.SalesOrderItems.Add(replacement);
        }

        order.CustomerId = request.CustomerId;
        order.OrderDate = request.OrderDate;
        order.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        order.Status = status;
        order.Notes = Null(request.Notes);
        order.GstType = Null(request.GstType) ?? "IGST";
        order.DiscountAmount = request.DiscountAmount;
        order.FreightAmount = request.FreightAmount;
        OrderCalculator.Recalculate(order);
    }

    private async Task SaveNewOrder(
        Guid orderId,
        OrderRequest request,
        string status,
        CancellationToken cancellationToken)
    {
        if (!IsCancelled(status))
        {
            var stockError = await GetStockAvailabilityErrorAsync(
                GetProductQuantities(request.Items),
                EmptyProductQuantities,
                cancellationToken);
            if (stockError is not null)
            {
                throw new InsufficientStockException(stockError);
            }
        }

        var existingOrderNumbers = await database.SalesOrders.AsNoTracking()
            .Select(order => order.OrderNumber)
            .ToListAsync(cancellationToken);
        
        var isNonGst = string.Equals(request.GstType, "None", StringComparison.OrdinalIgnoreCase);
        var orderNumber = isNonGst ? $"ORD-{orderId.ToString()[..8].ToUpperInvariant()}" : GetNextOrderNumber(request.OrderDate, existingOrderNumbers);

        var order = new SalesOrder
        {
            Id = orderId,
            OrderNumber = orderNumber,
        };
        // Track the parent before adding line items. That gives EF a principal
        // for relationship fix-up and ensures each new item is tracked as an
        // insert rather than an update.
        database.SalesOrders.Add(order);
        ReplaceOrderValues(order, request, status);
        
        if (request.PaidAmount > 0)
        {
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                PaymentNumber = DocumentNumbers.New("PAY"),
                CustomerId = request.CustomerId,
                SalesOrder = order,
                Amount = request.PaidAmount.Value,
                PaymentDate = request.OrderDate,
                Method = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "UPI" : request.PaymentMethod,
                IsAdvance = false
            };
            database.Payments.Add(payment);
        }

        await OrderCalculator.SyncOrderCompletionAsync(database, order, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<SalesOrder?> GetOrderGraph(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<SalesOrder> query = database.SalesOrders
            .Include(order => order.Customer)
            .Include(order => order.Items)
            .Include(order => order.Payments)
            .AsSplitQuery();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    private static bool CannotCancel(string status, SalesOrder order) =>
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) && order.Payments.Count > 0;

    private static bool IsCancelled(string status) =>
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<Guid, decimal> EmptyProductQuantities =
        new Dictionary<Guid, decimal>();

    private static Dictionary<Guid, decimal> GetProductQuantities(IEnumerable<OrderItemRequest> items)
    {
        var quantities = new Dictionary<Guid, decimal>();
        foreach (var item in items)
        {
            if (!item.ProductId.HasValue)
            {
                continue;
            }

            quantities[item.ProductId.Value] = quantities.GetValueOrDefault(item.ProductId.Value) + item.Quantity;
        }

        return quantities;
    }

    private static Dictionary<Guid, decimal> GetProductQuantities(IEnumerable<SalesOrderItem> items)
    {
        var quantities = new Dictionary<Guid, decimal>();
        foreach (var item in items)
        {
            if (!item.ProductId.HasValue)
            {
                continue;
            }

            quantities[item.ProductId.Value] = quantities.GetValueOrDefault(item.ProductId.Value) + item.Quantity;
        }

        return quantities;
    }

    private async Task<string?> GetStockAvailabilityErrorAsync(
        IReadOnlyDictionary<Guid, decimal> requestedQuantities,
        IReadOnlyDictionary<Guid, decimal> currentAllocation,
        CancellationToken cancellationToken)
    {
        if (requestedQuantities.Count == 0)
        {
            return null;
        }

        var productIds = requestedQuantities.Keys.ToList();
        var products = await database.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new ProductStock(product.Id, product.Name, product.Unit, product.QuantityOnHand, product.IsActive))
            .ToListAsync(cancellationToken);
        var stockByProductId = products.ToDictionary(product => product.Id);

        foreach (var (productId, requestedQuantity) in requestedQuantities)
        {
            if (!stockByProductId.TryGetValue(productId, out var product) || !product.IsActive)
            {
                return "A selected product is no longer available.";
            }

            var alreadyAllocated = currentAllocation.GetValueOrDefault(productId);
            var additionalQuantity = requestedQuantity - alreadyAllocated;
            if (additionalQuantity <= product.QuantityOnHand)
            {
                continue;
            }

            var availableForThisOrder = product.QuantityOnHand + alreadyAllocated;
            return $"Insufficient stock for {product.Name}. Available stock is {availableForThisOrder:0.###} {product.Unit}; requested quantity is {requestedQuantity:0.###} {product.Unit}.";
        }

        return null;
    }

    private static string GetNextOrderNumber(DateOnly orderDate, IEnumerable<string> orderNumbers)
    {
        var financialYear = GetFinancialYear(orderDate);
        var maxNumber = orderNumbers
            .Select(number => TryGetInvoiceSequence(number, financialYear))
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .DefaultIfEmpty(0)
            .Max();
        if (maxNumber == int.MaxValue)
        {
            throw new OrderNumberExhaustedException();
        }

        return $"{maxNumber + 1:D2}/{financialYear}";
    }

    private static string GetFinancialYear(DateOnly date)
    {
        // India uses an April-to-March financial year. For example, an August
        // 2026 order belongs to FY 2026-27 while a January 2027 order remains
        // in the same FY.
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{startYear % 100:D2}-{(startYear + 1) % 100:D2}";
    }

    private static int? TryGetInvoiceSequence(string orderNumber, string financialYear)
    {
        var suffix = $"/{financialYear}";
        if (!orderNumber.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        var sequenceText = orderNumber[..^suffix.Length];
        return int.TryParse(sequenceText, out var sequence) && sequence > 0
            ? sequence
            : null;
    }

    private sealed class OrderNumberExhaustedException : Exception
    {
    }

    private sealed class InsufficientStockException(string message) : Exception(message)
    {
    }

    private sealed record ProductStock(
        Guid Id,
        string Name,
        string Unit,
        decimal QuantityOnHand,
        bool IsActive);

    private ConflictObjectResult OrderNumberExhaustedConflict() => Conflict(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "No additional invoice numbers are available for this financial year.",
        Detail = "Contact support to configure a new invoice-number sequence.",
    });

    private ConflictObjectResult PaidOrderCancellationConflict() => Conflict(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "This order has recorded payments.",
        Detail = "Reverse or reassign the payments before cancelling the order."
    });

    private static OrderSummaryDto ToSummaryDto(SalesOrder order)
    {
        var paid = order.Payments.Where(payment => !payment.IsAdvance).Sum(payment => payment.Amount);
        return new OrderSummaryDto(
            order.Id, order.OrderNumber, order.CustomerId, DtoMappings.DisplayCustomerName(order.Customer),
            order.Items.OrderBy(item => item.Id).Select(item => item.Description).FirstOrDefault() ?? "—",
            order.OrderDate, order.ExpectedDeliveryDate, order.Status, order.GrandTotal,
            paid, Math.Max(order.GrandTotal - paid, 0m), order.UpdatedAtUtc);
    }

    private static OrderDetailDto ToDetailDto(SalesOrder order, CompanyProfileDto? company = null)
    {
        var summary = ToSummaryDto(order);
        return new OrderDetailDto(
            order.Id, order.OrderNumber, order.CustomerId, summary.CustomerName, summary.ProductName, order.OrderDate,
            order.ExpectedDeliveryDate, order.Status, order.Notes, order.Subtotal, order.DiscountAmount,
            order.FreightAmount, order.TaxAmount, order.GstType, order.GrandTotal, summary.PaidAmount, summary.Outstanding,
            order.Items.OrderBy(item => item.Id).Select(item => item.ToDto()).ToList(),
            order.CreatedAtUtc, order.UpdatedAtUtc, company);
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
