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

        var query = database.PurchaseRecords.AsNoTracking()
            .Include(p => p.Payments)
            .AsQueryable();

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
            .Include(p => p.Payments)
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

        if (request.InitialPaidAmount is < 0m)
        {
            return ValidationError(nameof(request.InitialPaidAmount), "Initial payment cannot be negative.");
        }

        if (request.InitialPaidAmount.HasValue && request.InitialPaidAmount.Value > request.PurchaseAmount!.Value)
        {
            return ValidationError(nameof(request.InitialPaidAmount),
                $"Initial payment amount ({request.InitialPaidAmount.Value:N2}) cannot exceed total purchase amount ({request.PurchaseAmount.Value:N2}).");
        }

        var purchase = new PurchaseRecord
        {
            PurchaseNumber = DocumentNumbers.New("PUR"),
        };
        Apply(purchase, request);

        if (request.InitialPaidAmount.HasValue && request.InitialPaidAmount.Value > 0m)
        {
            var payment = new PurchasePayment
            {
                PaymentNumber = DocumentNumbers.New("PPAY"),
                Amount = request.InitialPaidAmount.Value,
                PaymentDate = request.PurchaseDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
                PaymentMode = string.IsNullOrWhiteSpace(request.PaymentMode) ? "Cash" : request.PaymentMode.Trim(),
                ReferenceNumber = Null(request.PaymentReference),
                Notes = Null(request.PaymentNotes),
                PurchaseRecordId = purchase.Id,
            };
            purchase.Payments.Add(payment);

            purchase.PaymentStatus = request.InitialPaidAmount.Value >= purchase.PurchaseAmount
                ? "Fully Paid"
                : "Partially Paid";
        }
        else if (request.InitialPaidAmount.HasValue && request.InitialPaidAmount.Value == 0m)
        {
            purchase.PaymentStatus = "Unpaid";
        }

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
            .Include(p => p.Payments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (purchase is null)
        {
            return NotFound();
        }

        Apply(purchase, request);

        // Recalculate status based on existing recorded payments
        var totalPaid = purchase.Payments.Sum(p => p.Amount);
        if (purchase.Payments.Count > 0)
        {
            purchase.PaymentStatus = totalPaid >= purchase.PurchaseAmount ? "Fully Paid" : (totalPaid > 0 ? "Partially Paid" : "Unpaid");
        }

        await database.SaveChangesAsync(cancellationToken);
        return Ok(purchase.ToDto());
    }

    [HttpPost("{id:guid}/payments")]
    [ProducesResponseType(typeof(PurchaseRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseRecordDto>> RecordPayment(
        Guid id,
        RecordPurchasePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var purchase = await database.PurchaseRecords
            .Include(p => p.Payments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (purchase is null)
        {
            return NotFound();
        }

        if (request.Amount <= 0m)
        {
            return ValidationError(nameof(request.Amount), "Payment amount must be greater than zero.");
        }

        var currentPaid = purchase.Payments.Sum(p => p.Amount);
        var pending = Math.Max(0m, purchase.PurchaseAmount - currentPaid);

        if (request.Amount > pending)
        {
            return ValidationError(nameof(request.Amount),
                $"Payment amount of {request.Amount:N2} exceeds the remaining pending balance of {pending:N2}.");
        }

        var payment = new PurchasePayment
        {
            PaymentNumber = DocumentNumbers.New("PPAY"),
            PurchaseRecordId = purchase.Id,
            Amount = request.Amount,
            PaymentDate = request.PaymentDate,
            PaymentMode = string.IsNullOrWhiteSpace(request.PaymentMode) ? "Cash" : request.PaymentMode.Trim(),
            ReferenceNumber = Null(request.ReferenceNumber),
            Notes = Null(request.Notes),
        };

        database.PurchasePayments.Add(payment);

        var newPaid = currentPaid + request.Amount;
        purchase.PaymentStatus = newPaid >= purchase.PurchaseAmount ? "Fully Paid" : "Partially Paid";

        await database.SaveChangesAsync(cancellationToken);
        return Ok(purchase.ToDto());
    }

    [HttpDelete("{id:guid}/payments/{paymentId:guid}")]
    [ProducesResponseType(typeof(PurchaseRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PurchaseRecordDto>> DeletePayment(
        Guid id,
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        var purchase = await database.PurchaseRecords
            .Include(p => p.Payments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (purchase is null)
        {
            return NotFound();
        }

        var payment = purchase.Payments.FirstOrDefault(p => p.Id == paymentId);
        if (payment is null)
        {
            return NotFound();
        }

        database.PurchasePayments.Remove(payment);

        var remainingPaid = purchase.Payments.Where(p => p.Id != paymentId).Sum(p => p.Amount);
        if (remainingPaid <= 0m)
        {
            purchase.PaymentStatus = "Unpaid";
        }
        else if (remainingPaid >= purchase.PurchaseAmount)
        {
            purchase.PaymentStatus = "Fully Paid";
        }
        else
        {
            purchase.PaymentStatus = "Partially Paid";
        }

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
            .Include(p => p.Payments)
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

        if (request.UnitPrice.HasValue && request.UnitPrice.Value < 0m)
        {
            return (nameof(request.UnitPrice), "Unit price cannot be negative.");
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
        purchase.UnitPrice = request.UnitPrice;
        purchase.PurchaseAmount = request.PurchaseAmount!.Value;
        purchase.GstAmount = request.GstAmount;
        purchase.GstRate = request.GstRate;
        if (!string.IsNullOrWhiteSpace(request.PaymentStatus))
        {
            purchase.PaymentStatus = NormalizePaymentStatus(request.PaymentStatus);
        }
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
