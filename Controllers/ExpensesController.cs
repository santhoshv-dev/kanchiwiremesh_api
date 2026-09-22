using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/expenses")]
public sealed class ExpensesController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ExpenseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ExpenseDto>>> GetExpenses(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);

        var query = database.Expenses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e =>
                e.ExpenseNumber.ToLower().Contains(term) ||
                e.Description.ToLower().Contains(term) ||
                e.Category.ToLower().Contains(term) ||
                (e.PaidTo != null && e.PaidTo.ToLower().Contains(term)) ||
                (e.ReferenceNumber != null && e.ReferenceNumber.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            var cat = category.Trim().ToLower();
            query = query.Where(e => e.Category.ToLower() == cat);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(e => e.ExpenseDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(e => e.ExpenseDate <= toDate.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<ExpenseDto>(
            items.Select(e => e.ToDto()).ToList(),
            page,
            pageSize,
            totalCount));
    }

    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetCategories(CancellationToken cancellationToken)
    {
        var existingCategories = await database.Expenses.AsNoTracking()
            .Select(e => e.Category)
            .Distinct()
            .ToListAsync(cancellationToken);

        var all = WorkflowValues.ExpenseCategories
            .Union(existingCategories, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();

        return Ok(all);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ExpenseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseDto>> GetExpense(Guid id, CancellationToken cancellationToken)
    {
        var expense = await database.Expenses.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return expense is null ? NotFound() : Ok(expense.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ExpenseDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ExpenseDto>> CreateExpense(
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequiredFields(request);
        if (validationError is not null)
        {
            return ValidationError(validationError.Value.Field, validationError.Value.Message);
        }

        var expense = new Expense
        {
            ExpenseNumber = DocumentNumbers.New("EXP"),
        };
        Apply(expense, request);
        database.Expenses.Add(expense);
        await database.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetExpense), new { id = expense.Id }, expense.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ExpenseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseDto>> UpdateExpense(
        Guid id,
        ExpenseRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequiredFields(request);
        if (validationError is not null)
        {
            return ValidationError(validationError.Value.Field, validationError.Value.Message);
        }

        var expense = await database.Expenses
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        Apply(expense, request);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(expense.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteExpense(Guid id, CancellationToken cancellationToken)
    {
        var expense = await database.Expenses
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (expense is null)
        {
            return NotFound();
        }

        database.Expenses.Remove(expense);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static (string Field, string Message)? ValidateRequiredFields(ExpenseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Category))
        {
            return (nameof(request.Category), "Expense category is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return (nameof(request.Description), "Expense description/reason is required.");
        }

        if (request.Amount <= 0m)
        {
            return (nameof(request.Amount), "Expense amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMode))
        {
            return (nameof(request.PaymentMode), "Payment mode is required.");
        }

        return null;
    }

    private static void Apply(Expense expense, ExpenseRequest request)
    {
        expense.ExpenseDate = request.ExpenseDate;
        expense.Category = request.Category.Trim();
        expense.Description = request.Description.Trim();
        expense.Amount = request.Amount;
        expense.PaymentMode = request.PaymentMode.Trim();
        expense.PaidTo = Null(request.PaidTo);
        expense.ReferenceNumber = Null(request.ReferenceNumber);
        expense.Notes = Null(request.Notes);
        expense.AttachmentUrl = Null(request.AttachmentUrl);
    }

    private static string? Null(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
