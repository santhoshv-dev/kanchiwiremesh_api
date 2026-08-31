using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

/// <summary>
/// Maintains a standalone history of externally purchased products and raw
/// materials. These records are intentionally separate from product stock,
/// sales orders, customers, and product transactions.
/// </summary>
[Route("api/purchases")]
public sealed class PurchasesController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PurchaseRecordDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PurchaseRecordDto>>> GetPurchases(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);

        var query = database.PurchaseRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(purchase =>
                purchase.PurchaseNumber.ToLower().Contains(term) ||
                purchase.ProductName.ToLower().Contains(term) ||
                (purchase.ProductCode ?? string.Empty).ToLower().Contains(term) ||
                (purchase.BuyerName ?? string.Empty).ToLower().Contains(term) ||
                (purchase.BuyerContactNumber ?? string.Empty).Contains(term) ||
                (purchase.BuyerGstNumber ?? string.Empty).ToLower().Contains(term) ||
                (purchase.SupplierName ?? string.Empty).ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var purchases = await query
            .OrderByDescending(purchase => purchase.PurchaseDate)
            .ThenByDescending(purchase => purchase.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<PurchaseRecordDto>(
            purchases.Select(purchase => purchase.ToDto()).ToList(),
            page,
            pageSize,
            totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PurchaseRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseRecordDto>> GetPurchase(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var purchase = await database.PurchaseRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return purchase is null ? NotFound() : Ok(purchase.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(PurchaseRecordDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PurchaseRecordDto>> CreatePurchase(
        PurchaseRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRequiredFields(request);
        if (validationError is not null)
        {
            return ValidationError(validationError.Value.Field, validationError.Value.Message);
        }

        var purchase = new PurchaseRecord
        {
            PurchaseNumber = DocumentNumbers.New("PUR"),
        };
        Apply(purchase, request);
        database.PurchaseRecords.Add(purchase);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetPurchase), new { purchase.Id }, purchase.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PurchaseRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseRecordDto>> UpdatePurchase(
        Guid id,
        PurchaseRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRequiredFields(request);
        if (validationError is not null)
        {
            return ValidationError(validationError.Value.Field, validationError.Value.Message);
        }

        var purchase = await database.PurchaseRecords
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (purchase is null)
        {
            return NotFound();
        }

        Apply(purchase, request);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(purchase.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePurchase(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var purchase = await database.PurchaseRecords
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (purchase is null)
        {
            return NotFound();
        }

        database.PurchaseRecords.Remove(purchase);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static (string Field, string Message)? ValidateRequiredFields(PurchaseRecordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProductName))
        {
            return (nameof(request.ProductName), "Product/raw material name is required.");
        }

        if (!request.PurchaseDate.HasValue || request.PurchaseDate.Value == DateOnly.MinValue)
        {
            return (nameof(request.PurchaseDate), "Purchase date is required.");
        }

        if (!request.QuantityPurchased.HasValue || request.QuantityPurchased.Value <= 0m)
        {
            return (nameof(request.QuantityPurchased), "Quantity purchased must be greater than zero.");
        }

        if (!FitsScale(request.QuantityPurchased.Value, 0.001m))
        {
            return (nameof(request.QuantityPurchased), "Quantity purchased can have at most 3 decimal places.");
        }

        if (!request.PurchaseAmount.HasValue || request.PurchaseAmount.Value < 0m)
        {
            return (nameof(request.PurchaseAmount), "Purchase amount must be zero or greater.");
        }

        if (!FitsScale(request.PurchaseAmount.Value, 0.01m))
        {
            return (nameof(request.PurchaseAmount), "Purchase amount can have at most 2 decimal places.");
        }

        if (request.GstAmount is < 0m)
        {
            return (nameof(request.GstAmount), "GST amount must be zero or greater.");
        }

        if (request.GstAmount.HasValue && !FitsScale(request.GstAmount.Value, 0.01m))
        {
            return (nameof(request.GstAmount), "GST amount can have at most 2 decimal places.");
        }

        if (request.GstRate is < 0m or > 100m)
        {
            return (nameof(request.GstRate), "GST rate must be between 0 and 100.");
        }

        if (request.GstRate.HasValue && !FitsScale(request.GstRate.Value, 0.01m))
        {
            return (nameof(request.GstRate), "GST rate can have at most 2 decimal places.");
        }

        return null;
    }

    // SQL Server stores these values as fixed-scale decimals. Reject rather
    // than silently round data supplied by a client outside the Flutter form.
    private static bool FitsScale(decimal value, decimal smallestUnit) =>
        value % smallestUnit == 0m;

    private static void Apply(PurchaseRecord purchase, PurchaseRecordRequest request)
    {
        purchase.ProductName = request.ProductName.Trim();
        purchase.ProductCode = Null(request.ProductCode);
        purchase.BuyerName = Null(request.BuyerName);
        purchase.BuyerContactNumber = Null(request.BuyerContactNumber);
        purchase.BuyerGstNumber = Null(request.BuyerGstNumber);
        purchase.BuyerLocation = Null(request.BuyerLocation);
        purchase.SupplierName = Null(request.SupplierName);
        purchase.PurchaseDate = request.PurchaseDate!.Value;
        purchase.QuantityPurchased = request.QuantityPurchased!.Value;
        purchase.PurchaseAmount = request.PurchaseAmount!.Value;
        purchase.GstAmount = request.GstAmount;
        purchase.GstRate = request.GstRate;
        purchase.PaymentStatus = NormalizePaymentStatus(request.PaymentStatus);
        purchase.Notes = Null(request.Notes);
    }

    private static string NormalizePaymentStatus(string? value)
    {
        var trimmed = Null(value);
        if (trimmed is null)
        {
            return "Pending";
        }

        return WorkflowValues.TryNormalize(
            trimmed,
            WorkflowValues.PurchasePaymentStatuses,
            out var normalized)
            ? normalized
            : trimmed;
    }

    private static string? Null(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
