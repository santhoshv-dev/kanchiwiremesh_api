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

    [Fact]
    public async Task GetDashboard_ReturnsCurrentMonthSalesAndReceived()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-MONTHLY",
            ContactName = "Monthly Customer",
            Phone = "9876543210",
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var lastMonth = startOfThisMonth.AddMonths(-1);

        // Current month order
        database.SalesOrders.Add(new SalesOrder
        {
            OrderNumber = "ORD-THIS-MONTH",
            Customer = customer,
            Status = "New",
            OrderDate = startOfThisMonth,
            GrandTotal = 500m,
        });

        // Previous month order (should NOT be counted in MonthlySales)
        database.SalesOrders.Add(new SalesOrder
        {
            OrderNumber = "ORD-LAST-MONTH",
            Customer = customer,
            Status = "Completed",
            OrderDate = lastMonth,
            GrandTotal = 300m,
        });

        // Current month payment
        database.Payments.Add(new Payment
        {
            PaymentNumber = "PAY-THIS-MONTH",
            Customer = customer,
            PaymentDate = startOfThisMonth,
            Amount = 200m,
        });

        // Previous month payment (should NOT be counted in MonthlyReceived)
        database.Payments.Add(new Payment
        {
            PaymentNumber = "PAY-LAST-MONTH",
            Customer = customer,
            PaymentDate = lastMonth,
            Amount = 150m,
        });

        // Product with amount
        database.Products.Add(new Product
        {
            ProductCode = "P-CRIMPED",
            Name = "Crimped Mesh 1",
            Category = "Crimped Wire Mesh",
            Rate = 50m,
            QuantityOnHand = 10m,
            IsActive = true,
        });

        await database.SaveChangesAsync();

        var response = await new DashboardController(database).GetDashboard(CancellationToken.None);
        var result = Assert.IsType<OkObjectResult>(response.Result);
        var summary = Assert.IsType<DashboardSummaryDto>(result.Value);

        Assert.Equal(500m, summary.MonthlySales);
        Assert.Equal(200m, summary.MonthlyReceived);
        Assert.Equal(500m, summary.TotalProductsAmount); // 10 * 50
    }

    [Fact]
    public async Task GetMonthlySales_SplitsGstAndNonGstOrders()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-SALES-SPLIT",
            ContactName = "Sales Split Customer",
            Phone = "9876543210",
            GstNumber = "33ABCDE1234F1Z5",
        };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        database.SalesOrders.AddRange(
            new SalesOrder
            {
                OrderNumber = "GST-CURRENT",
                Customer = customer,
                Status = "Completed",
                OrderDate = currentMonth,
                GstType = "IGST",
                TaxAmount = 180m,
                GrandTotal = 1_180m,
            },
            new SalesOrder
            {
                OrderNumber = "NON-GST-CURRENT",
                Customer = customer,
                Status = "New",
                OrderDate = currentMonth,
                GstType = "None",
                GrandTotal = 500m,
            },
            new SalesOrder
            {
                OrderNumber = "CANCELLED-CURRENT",
                Customer = customer,
                Status = "Cancelled",
                OrderDate = currentMonth,
                GstType = "IGST",
                TaxAmount = 90m,
                GrandTotal = 590m,
            },
            new SalesOrder
            {
                OrderNumber = "GST-PREVIOUS",
                Customer = customer,
                Status = "Completed",
                OrderDate = currentMonth.AddMonths(-1),
                GstType = "IGST",
                TaxAmount = 36m,
                GrandTotal = 236m,
            });
        await database.SaveChangesAsync();

        var response = await new DashboardController(database)
            .GetMonthlySales(cancellationToken: CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(response.Result);
        var breakdown = Assert.IsType<MonthlySalesBreakdownDto>(result.Value);
        Assert.Equal(currentMonth, breakdown.Month);
        Assert.Equal(1_680m, breakdown.TotalSales);
        Assert.Equal(1_180m, breakdown.GstSales);
        Assert.Equal(500m, breakdown.NonGstSales);
        Assert.Equal(180m, breakdown.GstCollected);
        Assert.Equal(1_000m, breakdown.GstSubtotal);
        Assert.Equal(1, breakdown.GstOrderCount);
        Assert.Equal(1, breakdown.NonGstOrderCount);
        Assert.Equal(2, breakdown.Orders.Count);
        Assert.Equal("33ABCDE1234F1Z5", breakdown.Orders.First().CustomerGstNumber);
        Assert.Equal(1_000m, breakdown.Orders.First().Subtotal);
    }

    [Fact]
    public async Task ProductsController_GetCategorySummary_GroupsByCategory()
    {
        await using var database = CreateDatabase();
        database.Products.AddRange(
            new Product
            {
                ProductCode = "P1",
                Name = "Wire Mesh 1",
                Category = "Crimped Mesh",
                Rate = 100m,
                QuantityOnHand = 5m,
                IsActive = true,
            },
            new Product
            {
                ProductCode = "P2",
                Name = "Wire Mesh 2",
                Category = "Crimped Mesh",
                Rate = 50m,
                QuantityOnHand = 10m,
                IsActive = true,
            },
            new Product
            {
                ProductCode = "P3",
                Name = "Welded 1",
                Category = "Welded Mesh",
                Rate = 200m,
                QuantityOnHand = 2m,
                IsActive = true,
            });
        await database.SaveChangesAsync();

        var response = await new ProductsController(database).GetCategorySummary(CancellationToken.None);
        var result = Assert.IsType<OkObjectResult>(response.Result);
        var categories = Assert.IsAssignableFrom<IReadOnlyList<ProductCategorySummaryDto>>(result.Value);

        Assert.Equal(2, categories.Count);
        var crimped = categories.First(c => c.Category == "Crimped Mesh");
        Assert.Equal(1000m, crimped.TotalAmount); // (5*100) + (10*50) = 500 + 500 = 1000
        Assert.Equal(2, crimped.ProductCount);

        var welded = categories.First(c => c.Category == "Welded Mesh");
        Assert.Equal(400m, welded.TotalAmount); // 2*200 = 400
        Assert.Equal(1, welded.ProductCount);
    }

    [Fact]
    public async Task ProductsController_GetCategorySummary_UsesQuantityOnHandNotRawMaterialCapacity()
    {
        await using var database = CreateDatabase();
        var rawMaterial = new RawMaterial
        {
            Name = "GI Wire",
            Unit = "kg",
            TotalStock = 1000m,
            UsedStock = 0m,
            IsActive = true,
        };
        database.RawMaterials.Add(rawMaterial);

        var product = new Product
        {
            ProductCode = "P-RAW",
            Name = "Wire Mesh with Raw Material",
            Category = "Crimped Mesh",
            Rate = 100m,
            QuantityOnHand = 5m,
            IsActive = true,
        };
        product.RawMaterials.Add(new ProductRawMaterial
        {
            RawMaterial = rawMaterial,
            ConsumptionQuantity = 10m, // 1000 / 10 = 100 available pieces capacity
        });
        database.Products.Add(product);
        await database.SaveChangesAsync();

        var response = await new ProductsController(database).GetCategorySummary(CancellationToken.None);
        var result = Assert.IsType<OkObjectResult>(response.Result);
        var categories = Assert.IsAssignableFrom<IReadOnlyList<ProductCategorySummaryDto>>(result.Value);

        var crimped = categories.First(c => c.Category == "Crimped Mesh");
        // Stock report must reflect physical stock on hand (5 * 100 = 500), not raw material capacity (100 * 100 = 10000)
        Assert.Equal(500m, crimped.TotalAmount);
        Assert.Equal(5m, crimped.TotalPieces);
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
