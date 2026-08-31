using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

/// <summary>
/// Manages product-specific transactions (Sale, Purchase, Return, Adjustment).
/// </summary>
[Route("api/products/{productId:guid}/transactions")]
public sealed class ProductTransactionsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductTransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<ProductTransactionDto>>> GetTransactions(
        Guid productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var product = await database.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null) return NotFound();

        (page, pageSize) = NormalizePage(page, pageSize);

        var query = database.ProductTransactions
            .AsNoTracking()
            .Where(s => s.ProductId == productId);

        var totalCount = await query.CountAsync(cancellationToken);
        var transactions = await query
            .OrderByDescending(s => s.TransactionDate)
            .ThenByDescending(s => s.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = transactions.Select(s => s.ToDto(product)).ToList();
        return Ok(new PagedResult<ProductTransactionDto>(items, page, pageSize, totalCount));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(ProductTransactionsSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionsSummaryDto>> GetSummary(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await database.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null) return NotFound();

        var stats = await database.ProductTransactions
            .AsNoTracking()
            .Where(s => s.ProductId == productId)
            .GroupBy(_ => productId)
            .Select(g => new
            {
                TotalQuantitySold = g.Where(x => x.TransactionType == "Sale").Sum(s => s.Quantity),
                TotalQuantityPurchased = g.Where(x => x.TransactionType == "Purchase").Sum(s => s.Quantity),
                TotalSalesAmount = g.Where(x => x.TransactionType == "Sale").Sum(s => s.Amount),
                TotalPurchasesAmount = g.Where(x => x.TransactionType == "Purchase").Sum(s => s.Amount),
                TransactionCount = g.Count(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new ProductTransactionsSummaryDto(
            product.Id,
            product.Name,
            product.ProductCode,
            product.Unit,
            stats?.TotalQuantitySold ?? 0m,
            stats?.TotalQuantityPurchased ?? 0m,
            stats?.TotalSalesAmount ?? 0m,
            stats?.TotalPurchasesAmount ?? 0m,
            stats?.TransactionCount ?? 0));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionDto>> GetTransaction(
        Guid productId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var product = await database.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null) return NotFound();

        var transaction = await database.ProductTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == id && s.ProductId == productId, cancellationToken);

        return transaction is null ? NotFound() : Ok(transaction.ToDto(product));
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionDto>> CreateTransaction(
        Guid productId,
        ProductTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await database.Products
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null) return NotFound();

        var prefix = request.TransactionType switch {
            "Sale" => "PS",
            "Purchase" => "PP",
            "Return" => "PR",
            "Adjustment" => "PA",
            _ => "PT"
        };

        var transaction = new ProductTransaction
        {
            TransactionNumber = DocumentNumbers.New(prefix),
            ProductId = productId,
            TransactionType = request.TransactionType,
            PartyName = NullOrTrim(request.PartyName),
            PartyMobile = NullOrTrim(request.PartyMobile),
            PartyLocation = NullOrTrim(request.PartyLocation),
            TransactionDate = request.TransactionDate,
            Quantity = request.Quantity,
            Amount = request.Amount,
            PaymentStatus = NullOrTrim(request.PaymentStatus) ?? "Paid",
            Notes = NullOrTrim(request.Notes),
        };

        ApplyStockChange(product, transaction, false);

        database.ProductTransactions.Add(transaction);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetTransaction), new { productId, id = transaction.Id }, transaction.ToDto(product));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductTransactionDto>> UpdateTransaction(
        Guid productId,
        Guid id,
        ProductTransactionRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await database.Products
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null) return NotFound();

        var transaction = await database.ProductTransactions
            .SingleOrDefaultAsync(s => s.Id == id && s.ProductId == productId, cancellationToken);

        if (transaction is null) return NotFound();

        // Revert old effect
        ApplyStockChange(product, transaction, true);

        transaction.TransactionType = request.TransactionType;
        transaction.PartyName = NullOrTrim(request.PartyName);
        transaction.PartyMobile = NullOrTrim(request.PartyMobile);
        transaction.PartyLocation = NullOrTrim(request.PartyLocation);
        transaction.TransactionDate = request.TransactionDate;
        transaction.Quantity = request.Quantity;
        transaction.Amount = request.Amount;
        transaction.PaymentStatus = NullOrTrim(request.PaymentStatus) ?? "Paid";
        transaction.Notes = NullOrTrim(request.Notes);

        // Apply new effect
        ApplyStockChange(product, transaction, false);

        await database.SaveChangesAsync(cancellationToken);
        return Ok(transaction.ToDto(product));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTransaction(
        Guid productId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var product = await database.Products
            .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
            
        if (product is null) return NotFound();

        var transaction = await database.ProductTransactions
            .SingleOrDefaultAsync(s => s.Id == id && s.ProductId == productId, cancellationToken);

        if (transaction is null) return NotFound();

        ApplyStockChange(product, transaction, true);

        database.ProductTransactions.Remove(transaction);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private void ApplyStockChange(Product product, ProductTransaction transaction, bool isRevert)
    {
        int multiplier = isRevert ? -1 : 1;
        
        switch (transaction.TransactionType)
        {
            case "Sale":
                product.QuantityOnHand -= (transaction.Quantity * multiplier);
                product.TotalSold += (transaction.Quantity * multiplier);
                break;
            case "Purchase":
                product.QuantityOnHand += (transaction.Quantity * multiplier);
                product.TotalStockAdded += (transaction.Quantity * multiplier);
                break;
            case "Return":
                product.QuantityOnHand += (transaction.Quantity * multiplier);
                break;
            case "Adjustment":
                // Adjustments can be positive or negative depending on notes, or we just add it. 
                // We'll treat Quantity as a signed value for Adjustments (but since range is 0-999999, we'll treat it as additive unless we make quantity negative).
                // Let's assume adjustment adds to stock.
                product.QuantityOnHand += (transaction.Quantity * multiplier);
                break;
        }
    }

    private static string? NullOrTrim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
