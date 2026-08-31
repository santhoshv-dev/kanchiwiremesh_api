using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/dashboard")]
public sealed class DashboardController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboard(CancellationToken cancellationToken)
    {
        var customerCount = await database.Customers.CountAsync(customer => customer.IsActive, cancellationToken);
        var activeProductCount = await database.Products.CountAsync(product => product.IsActive, cancellationToken);
        var newEnquiryCount = await database.Enquiries.CountAsync(enquiry => enquiry.Status == "New", cancellationToken);
        var completedOrderCount = await database.SalesOrders.CountAsync(order => order.Status == "Delivered", cancellationToken);
        var pendingOrderCount = await database.SalesOrders.CountAsync(order =>
            order.Status == "New" || order.Status == "Processing" || order.Status == "Ready" || order.Status == "Dispatched",
            cancellationToken);

        var currentMonth = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstSalesMonth = new DateOnly(currentMonth.Year, currentMonth.Month, 1).AddMonths(-11);
        var firstMonthAfterSalesRange = firstSalesMonth.AddMonths(12);
        var monthlySales = await database.SalesOrders.AsNoTracking()
            .Where(order => order.Status != "Cancelled" &&
                order.OrderDate >= firstSalesMonth && order.OrderDate < firstMonthAfterSalesRange)
            .GroupBy(order => new { order.OrderDate.Year, order.OrderDate.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Amount = group.Sum(order => order.GrandTotal),
            })
            .ToListAsync(cancellationToken);
        var monthlySalesByMonth = monthlySales.ToDictionary(
            item => new DateOnly(item.Year, item.Month, 1),
            item => item.Amount);
        var salesBars = Enumerable.Range(0, 12)
            .Select(offset => monthlySalesByMonth.GetValueOrDefault(firstSalesMonth.AddMonths(offset), 0m))
            .ToList();

        var recentOrders = await database.SalesOrders.AsNoTracking()
            .Where(order => order.Status != "Cancelled")
            .Include(order => order.Customer)
            .Include(order => order.Payments)
            .Include(order => order.Items)
            .AsSplitQuery()
            .OrderByDescending(order => order.OrderDate)
            .ThenByDescending(order => order.CreatedAtUtc)
            .Take(5)
            .ToListAsync(cancellationToken);
        var totalOpeningBalances = await database.Customers
            .Where(customer => customer.IsActive)
            .SumAsync(customer => (decimal?)customer.OpeningBalance, cancellationToken) ?? 0m;
        var totalSales = (await database.SalesOrders
            .Where(order => order.Status != "Cancelled")
            .SumAsync(order => (decimal?)order.GrandTotal, cancellationToken) ?? 0m) + totalOpeningBalances;
        var totalReceived = await database.Payments
            .Where(payment => payment.SalesOrderId == null || payment.SalesOrder!.Status != "Cancelled")
            .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0m;
        var appliedToOrders = await database.Payments
            .Where(payment => !payment.IsAdvance &&
                payment.SalesOrderId != null &&
                payment.SalesOrder!.Status != "Cancelled")
            .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0m;
        var advanceBalance = await database.Payments
            .Where(payment => payment.IsAdvance &&
                (payment.SalesOrderId == null || payment.SalesOrder!.Status != "Cancelled"))
            .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0m;

        return Ok(new DashboardSummaryDto(
            customerCount,
            activeProductCount,
            newEnquiryCount,
            pendingOrderCount,
            completedOrderCount,
            totalSales,
            totalReceived,
            totalReceived,
            Math.Max(totalSales - totalReceived, 0m),
            advanceBalance,
            recentOrders.Select(ToSummaryDto).ToList(),
            salesBars));
    }

    private static OrderSummaryDto ToSummaryDto(SalesOrder order)
    {
        var paid = order.Payments.Where(payment => !payment.IsAdvance).Sum(payment => payment.Amount);
        return new OrderSummaryDto(
            order.Id, order.OrderNumber, order.CustomerId, DtoMappings.DisplayCustomerName(order.Customer),
            order.Items.OrderBy(item => item.Id).Select(item => item.Description).FirstOrDefault() ?? "—",
            order.OrderDate, order.ExpectedDeliveryDate, order.Status, order.GrandTotal,
            paid, Math.Max(order.GrandTotal - paid, 0m), order.UpdatedAtUtc);
    }
}
