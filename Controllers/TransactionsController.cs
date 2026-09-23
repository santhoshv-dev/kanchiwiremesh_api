using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/transactions")]
public sealed class TransactionsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(TransactionSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TransactionSummaryDto>> GetSummary(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken cancellationToken = default)
    {
        var paymentsQuery = database.Payments.AsNoTracking()
            .Where(p => p.SalesOrderId == null || p.SalesOrder!.Status != "Cancelled");
        var expensesQuery = database.Expenses.AsNoTracking().AsQueryable();
        var purchasePaymentsQuery = database.PurchasePayments.AsNoTracking().AsQueryable();

        if (fromDate.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(p => p.PaymentDate >= fromDate.Value);
            expensesQuery = expensesQuery.Where(e => e.ExpenseDate >= fromDate.Value);
            purchasePaymentsQuery = purchasePaymentsQuery.Where(p => p.PaymentDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(p => p.PaymentDate <= toDate.Value);
            expensesQuery = expensesQuery.Where(e => e.ExpenseDate <= toDate.Value);
            purchasePaymentsQuery = purchasePaymentsQuery.Where(p => p.PaymentDate <= toDate.Value);
        }

        var totalIncoming = await paymentsQuery.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
        var totalGeneralExpenses = await expensesQuery.SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0m;
        var totalSupplierPayments = await purchasePaymentsQuery.SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        var totalOutgoing = totalGeneralExpenses + totalSupplierPayments;
        var netBalance = totalIncoming - totalOutgoing;

        return Ok(new TransactionSummaryDto(
            totalIncoming,
            totalOutgoing,
            totalGeneralExpenses,
            totalSupplierPayments,
            netBalance,
            fromDate,
            toDate));
    }

    [HttpGet("report")]
    [ProducesResponseType(typeof(TransactionReportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TransactionReportDto>> GetReport(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] string? type,
        [FromQuery] string? search,
        CancellationToken cancellationToken = default)
    {
        var paymentsQuery = database.Payments.AsNoTracking()
            .Where(p => p.SalesOrderId == null || p.SalesOrder!.Status != "Cancelled")
            .Include(p => p.Customer)
            .AsQueryable();

        var expensesQuery = database.Expenses.AsNoTracking().AsQueryable();

        var purchasePaymentsQuery = database.PurchasePayments.AsNoTracking()
            .Include(p => p.PurchaseRecord)
            .AsQueryable();

        if (fromDate.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(p => p.PaymentDate >= fromDate.Value);
            expensesQuery = expensesQuery.Where(e => e.ExpenseDate >= fromDate.Value);
            purchasePaymentsQuery = purchasePaymentsQuery.Where(p => p.PaymentDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(p => p.PaymentDate <= toDate.Value);
            expensesQuery = expensesQuery.Where(e => e.ExpenseDate <= toDate.Value);
            purchasePaymentsQuery = purchasePaymentsQuery.Where(p => p.PaymentDate <= toDate.Value);
        }

        var customerPayments = await paymentsQuery.ToListAsync(cancellationToken);
        var expenses = await expensesQuery.ToListAsync(cancellationToken);
        var purchasePayments = await purchasePaymentsQuery.ToListAsync(cancellationToken);

        var rawList = new List<(DateOnly Date, DateTime CreatedAtUtc, TransactionItemDto Item)>();

        foreach (var p in customerPayments)
        {
            var customerName = DtoMappings.DisplayCustomerName(p.Customer);
            var item = new TransactionItemDto(
                p.Id,
                p.PaymentDate,
                "Customer Receipt",
                "Sales Receipt",
                $"Receipt #{p.PaymentNumber}" + (string.IsNullOrWhiteSpace(p.Notes) ? "" : $" - {p.Notes}"),
                customerName,
                p.Amount,
                0m,
                p.Method,
                p.Reference,
                0m,
                "Incoming",
                p.PaymentNumber);
            rawList.Add((p.PaymentDate, p.CreatedAtUtc, item));
        }

        foreach (var e in expenses)
        {
            var item = new TransactionItemDto(
                e.Id,
                e.ExpenseDate,
                "Operational Expense",
                e.Category,
                e.Description + (string.IsNullOrWhiteSpace(e.Notes) ? "" : $" - {e.Notes}"),
                e.PaidTo,
                0m,
                e.Amount,
                e.PaymentMode,
                e.ReferenceNumber,
                0m,
                "Outgoing",
                e.ExpenseNumber);
            rawList.Add((e.ExpenseDate, e.CreatedAtUtc, item));
        }

        foreach (var sp in purchasePayments)
        {
            var supplier = sp.PurchaseRecord.SupplierName ?? sp.PurchaseRecord.BuyerName;
            var item = new TransactionItemDto(
                sp.Id,
                sp.PaymentDate,
                "Supplier Payment",
                "Purchase Payment",
                $"Payment for {sp.PurchaseRecord.ProductName} ({sp.PurchaseRecord.PurchaseNumber})" +
                (string.IsNullOrWhiteSpace(sp.Notes) ? "" : $" - {sp.Notes}"),
                supplier,
                0m,
                sp.Amount,
                sp.PaymentMode,
                sp.ReferenceNumber,
                0m,
                "Outgoing",
                sp.PaymentNumber);
            rawList.Add((sp.PaymentDate, sp.CreatedAtUtc, item));
        }

        // Sort ascending by Date, then by Creation timestamp to calculate running balance
        var sorted = rawList
            .OrderBy(x => x.Date)
            .ThenBy(x => x.CreatedAtUtc)
            .Select(x => x.Item)
            .ToList();

        decimal runningBalance = 0m;
        var withRunningBalance = new List<TransactionItemDto>(sorted.Count);
        foreach (var t in sorted)
        {
            runningBalance += t.IncomingAmount - t.OutgoingAmount;
            withRunningBalance.Add(t with { RunningBalance = runningBalance });
        }

        // Apply filters if specified
        var filtered = withRunningBalance.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "All", StringComparison.OrdinalIgnoreCase))
        {
            var t = type.Trim().ToLower();
            if (t == "incoming" || t == "customer receipt")
            {
                filtered = filtered.Where(x => x.IncomingAmount > 0m);
            }
            else if (t == "outgoing")
            {
                filtered = filtered.Where(x => x.OutgoingAmount > 0m);
            }
            else if (t == "expense" || t == "operational expense")
            {
                filtered = filtered.Where(x => x.TransactionType == "Operational Expense");
            }
            else if (t == "supplier payment" || t == "purchase payment")
            {
                filtered = filtered.Where(x => x.TransactionType == "Supplier Payment");
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            filtered = filtered.Where(x =>
                x.Description.ToLower().Contains(term) ||
                x.Category.ToLower().Contains(term) ||
                (x.PartyName != null && x.PartyName.ToLower().Contains(term)) ||
                (x.ReferenceNumber != null && x.ReferenceNumber.ToLower().Contains(term)) ||
                (x.TransactionNumber != null && x.TransactionNumber.ToLower().Contains(term)) ||
                x.PaymentMode.ToLower().Contains(term));
        }

        var resultList = filtered.ToList();
        var totalIn = resultList.Sum(x => x.IncomingAmount);
        var totalOut = resultList.Sum(x => x.OutgoingAmount);

        return Ok(new TransactionReportDto(
            fromDate,
            toDate,
            totalIn,
            totalOut,
            totalIn - totalOut,
            resultList.Count,
            resultList));
    }
}
