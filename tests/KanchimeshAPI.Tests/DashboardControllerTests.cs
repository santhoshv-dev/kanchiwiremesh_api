using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class DashboardControllerTests
{
    [Fact]
    public async Task GetDashboard_UsesTheCurrentOrderWorkflowStatuses()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-DASHBOARD",
            ContactName = "Dashboard Customer",
            Phone = "9876543210",
        };
        database.SalesOrders.AddRange(
            NewOrder(customer, "DASH-PENDING", "Pending"),
            NewOrder(customer, "DASH-COMPLETED", "Completed"),
            NewOrder(customer, "DASH-DELIVERED", "Delivered"),
            NewOrder(customer, "DASH-CANCELLED", "Cancelled"));
        await database.SaveChangesAsync();

        var response = await new DashboardController(database).GetDashboard(CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var summary = Assert.IsType<DashboardSummaryDto>(result.Value);
        Assert.Equal(1, summary.PendingOrderCount);
        Assert.Equal(2, summary.CompletedOrderCount);
    }

    private static SalesOrder NewOrder(Customer customer, string number, string status) => new()
    {
        OrderNumber = number,
        Customer = customer,
        Status = status,
        OrderDate = new DateOnly(2026, 8, 29),
        GrandTotal = 100m,
    };

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
