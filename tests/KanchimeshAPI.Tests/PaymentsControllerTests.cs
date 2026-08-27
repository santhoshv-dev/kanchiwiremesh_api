using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class PaymentsControllerTests
{
    [Fact]
    public async Task CreatePayment_CreatesAndReturnsAPaymentForTheOrder()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-PAYMENT",
            ContactName = "Payment Customer",
            Phone = "9876543210",
        };
        var order = new SalesOrder
        {
            OrderNumber = "PAYMENT-ORDER",
            Customer = customer,
            Status = "New",
            OrderDate = new DateOnly(2026, 8, 27),
            GrandTotal = 500m,
        };
        database.SalesOrders.Add(order);
        await database.SaveChangesAsync();
        var controller = new PaymentsController(database);

        var response = await controller.CreatePayment(
            new PaymentRequest
            {
                CustomerId = customer.Id,
                SalesOrderId = order.Id,
                Amount = 500m,
                PaymentDate = new DateOnly(2026, 8, 27),
                Method = "UPI",
                IsAdvance = false,
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var payment = Assert.IsType<PaymentDto>(created.Value);
        Assert.Equal(customer.Id, payment.CustomerId);
        Assert.Equal(order.Id, payment.SalesOrderId);
        Assert.Equal(500m, payment.Amount);
        Assert.Equal("UPI", payment.Method);
        Assert.False(payment.IsAdvance);

        var persisted = await database.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(payment.Id, persisted.Id);
        Assert.Equal(order.Id, persisted.SalesOrderId);
    }

    [Fact]
    public async Task UpdatePayment_UpdatesAndReturnsThePayment()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-PAYMENT-UPDATE",
            ContactName = "Payment Update Customer",
            Phone = "9876543210",
        };
        var order = new SalesOrder
        {
            OrderNumber = "PAYMENT-UPDATE-ORDER",
            Customer = customer,
            Status = "New",
            OrderDate = new DateOnly(2026, 8, 27),
            GrandTotal = 500m,
        };
        var payment = new Payment
        {
            PaymentNumber = "PAY-UPDATE",
            Customer = customer,
            SalesOrder = order,
            Amount = 100m,
            PaymentDate = new DateOnly(2026, 8, 27),
            Method = "UPI",
        };
        database.Payments.Add(payment);
        await database.SaveChangesAsync();
        var controller = new PaymentsController(database);

        var response = await controller.UpdatePayment(
            payment.Id,
            new PaymentRequest
            {
                CustomerId = customer.Id,
                SalesOrderId = order.Id,
                Amount = 250m,
                PaymentDate = new DateOnly(2026, 8, 28),
                Method = "Bank Transfer",
                Reference = "BANK-123",
                IsAdvance = false,
            },
            CancellationToken.None);

        var updated = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<PaymentDto>(updated.Value);
        Assert.Equal(250m, result.Amount);
        Assert.Equal("Bank Transfer", result.Method);
        Assert.Equal("BANK-123", result.Reference);

        var persisted = await database.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(250m, persisted.Amount);
        Assert.Equal(new DateOnly(2026, 8, 28), persisted.PaymentDate);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
