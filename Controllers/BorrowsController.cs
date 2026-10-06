using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

/// <summary>
/// Independent borrowing ledger. Nothing in here reads or writes
/// <c>Payments</c>, so the existing payments APIs keep their own behaviour.
/// </summary>
[Route("api/borrows")]
public sealed class BorrowsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<BorrowDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BorrowDto>>> GetBorrows(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);

        var query = database.Borrows.AsNoTracking()
            .Include(borrow => borrow.Repayments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(borrow =>
                borrow.BorrowNumber.ToLower().Contains(term) ||
                borrow.BorrowerName.ToLower().Contains(term) ||
                borrow.PaymentMode.ToLower().Contains(term) ||
                (borrow.ReferenceNumber != null && borrow.ReferenceNumber.ToLower().Contains(term)) ||
                (borrow.Notes != null && borrow.Notes.ToLower().Contains(term)));
        }

        if (fromDate.HasValue)
        {
            query = query.Where(borrow => borrow.BorrowDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(borrow => borrow.BorrowDate <= toDate.Value);
        }

        var materialised = await query.ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(status) &&
            !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            materialised = materialised
                .Where(borrow => string.Equals(
                    borrow.ToDto().Status,
                    status.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var matching = materialised.Count;
        var items = materialised
            .OrderByDescending(borrow => borrow.BorrowDate)
            .ThenByDescending(borrow => borrow.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(borrow => borrow.ToDto())
            .ToList();

        return Ok(new PagedResult<BorrowDto>(items, page, pageSize, matching));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(BorrowSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BorrowSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var totalBorrowed = await database.Borrows.AsNoTracking()
            .SumAsync(borrow => (decimal?)borrow.Amount, cancellationToken) ?? 0m;
        var totalRepaid = await database.BorrowRepayments.AsNoTracking()
            .SumAsync(repayment => (decimal?)repayment.Amount, cancellationToken) ?? 0m;
        var borrowCount = await database.Borrows.AsNoTracking()
            .CountAsync(cancellationToken);
        var activeBorrowCount = await database.Borrows.AsNoTracking()
            .Include(borrow => borrow.Repayments)
            .Select(borrow => new { borrow.Amount, Repaid = borrow.Repayments.Sum(r => r.Amount) })
            .CountAsync(entry => entry.Amount > entry.Repaid, cancellationToken);

        return Ok(new BorrowSummaryDto(
            totalBorrowed,
            totalRepaid,
            Math.Max(totalBorrowed - totalRepaid, 0m),
            borrowCount,
            activeBorrowCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BorrowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BorrowDto>> GetBorrow(Guid id, CancellationToken cancellationToken)
    {
        var borrow = await database.Borrows.AsNoTracking()
            .Include(item => item.Repayments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return borrow is null ? NotFound() : Ok(borrow.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(BorrowDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<BorrowDto>> CreateBorrow(
        BorrowRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return ValidationError(validationError.Value.Field, validationError.Value.Message);
        }

        var borrow = new Borrow
        {
            BorrowNumber = await NextBorrowNumberAsync(cancellationToken),
        };
        Apply(borrow, request);
        database.Borrows.Add(borrow);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetBorrow), new { id = borrow.Id }, borrow.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BorrowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BorrowDto>> UpdateBorrow(
        Guid id,
        BorrowRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return ValidationError(validationError.Value.Field, validationError.Value.Message);
        }

        var borrow = await database.Borrows
            .Include(item => item.Repayments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (borrow is null)
        {
            return NotFound();
        }

        Apply(borrow, request);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(borrow.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBorrow(Guid id, CancellationToken cancellationToken)
    {
        var borrow = await database.Borrows
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (borrow is null)
        {
            return NotFound();
        }

        database.Borrows.Remove(borrow);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/repayments")]
    [ProducesResponseType(typeof(BorrowDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BorrowDto>> AddRepayment(
        Guid id,
        BorrowRepaymentRequest request,
        CancellationToken cancellationToken)
    {
        var borrow = await database.Borrows
            .Include(item => item.Repayments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (borrow is null)
        {
            return NotFound();
        }

        if (request.Amount <= 0m)
        {
            return ValidationError(nameof(request.Amount), "Repayment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMode))
        {
            return ValidationError(nameof(request.PaymentMode), "Payment method is required.");
        }

        var outstanding = borrow.Amount - borrow.Repayments.Sum(repayment => repayment.Amount);
        if (request.Amount > outstanding)
        {
            return ValidationError(
                nameof(request.Amount),
                $"Repayment amount exceeds the outstanding borrow balance of {outstanding:0.##}.");
        }

        var repayment = new BorrowRepayment
        {
            RepaymentNumber = await NextRepaymentNumberAsync(cancellationToken),
            BorrowId = borrow.Id,
        };
        Apply(repayment, request);
        database.BorrowRepayments.Add(repayment);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetBorrow), new { id = borrow.Id }, borrow.ToDto());
    }

    [HttpDelete("{id:guid}/repayments/{repaymentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRepayment(
        Guid id,
        Guid repaymentId,
        CancellationToken cancellationToken)
    {
        var repayment = await database.BorrowRepayments
            .SingleOrDefaultAsync(
                item => item.Id == repaymentId && item.BorrowId == id,
                cancellationToken);
        if (repayment is null)
        {
            return NotFound();
        }

        database.BorrowRepayments.Remove(repayment);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<string> NextBorrowNumberAsync(CancellationToken cancellationToken)
    {
        var nextNumber = await database.Borrows.CountAsync(cancellationToken) + 1;
        string borrowNumber;
        do
        {
            borrowNumber = $"BRW-{nextNumber}";
            nextNumber++;
        }
        while (await database.Borrows.AnyAsync(b => b.BorrowNumber == borrowNumber, cancellationToken));
        return borrowNumber;
    }

    private async Task<string> NextRepaymentNumberAsync(CancellationToken cancellationToken)
    {
        var nextNumber = await database.BorrowRepayments.CountAsync(cancellationToken) + 1;
        string repaymentNumber;
        do
        {
            repaymentNumber = $"RPA-{nextNumber}";
            nextNumber++;
        }
        while (await database.BorrowRepayments.AnyAsync(
            r => r.RepaymentNumber == repaymentNumber,
            cancellationToken));
        return repaymentNumber;
    }

    private static (string Field, string Message)? Validate(BorrowRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BorrowerName))
        {
            return (nameof(request.BorrowerName), "Borrower name is required.");
        }

        if (request.Amount <= 0m)
        {
            return (nameof(request.Amount), "Borrow amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMode))
        {
            return (nameof(request.PaymentMode), "Payment method is required.");
        }

        return null;
    }

    private static void Apply(Borrow borrow, BorrowRequest request)
    {
        borrow.BorrowerName = request.BorrowerName.Trim();
        borrow.BorrowDate = request.BorrowDate;
        borrow.Amount = request.Amount;
        borrow.PaymentMode = request.PaymentMode.Trim();
        borrow.ReferenceNumber = Null(request.ReferenceNumber);
        borrow.Notes = Null(request.Notes);
    }

    private static void Apply(BorrowRepayment repayment, BorrowRepaymentRequest request)
    {
        repayment.Amount = request.Amount;
        repayment.PaymentDate = request.PaymentDate;
        repayment.PaymentMode = request.PaymentMode.Trim();
        repayment.ReferenceNumber = Null(request.ReferenceNumber);
        repayment.Notes = Null(request.Notes);
    }

    private static string? Null(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

