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

    [Fact]
    public async Task CreatePayment_WithoutSalesOrder_UpdatesCustomerAndFinancialSummaries()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-DIRECT-PAYMENT",
            ContactName = "Direct Payment Customer",
            Phone = "9876543210",
            OpeningBalance = 1_000m,
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync();

        var payments = new PaymentsController(database);
        var response = await payments.CreatePayment(
            new PaymentRequest
            {
                CustomerId = customer.Id,
                Amount = 250m,
                PaymentDate = new DateOnly(2026, 8, 29),
                Method = "Cash",
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var payment = Assert.IsType<PaymentDto>(created.Value);
        Assert.Null(payment.SalesOrderId);
        Assert.Equal(250m, payment.Amount);

        var summaryResponse = await payments.GetSummary(CancellationToken.None);
        var summaryResult = Assert.IsType<OkObjectResult>(summaryResponse.Result);
        var summary = Assert.IsType<PaymentSummaryDto>(summaryResult.Value);
        Assert.Equal(1_000m, summary.TotalSales);
        Assert.Equal(250m, summary.TotalReceived);
        Assert.Equal(750m, summary.Outstanding);

        var dashboardResponse = await new DashboardController(database).GetDashboard(CancellationToken.None);
        var dashboardResult = Assert.IsType<OkObjectResult>(dashboardResponse.Result);
        var dashboard = Assert.IsType<DashboardSummaryDto>(dashboardResult.Value);
        Assert.Equal(250m, dashboard.TotalReceived);
        Assert.Equal(250m, dashboard.Received);
        Assert.Equal(750m, dashboard.Outstanding);

        var customerResponse = await new CustomersController(database).GetCustomer(customer.Id, CancellationToken.None);
        var customerResult = Assert.IsType<OkObjectResult>(customerResponse.Result);
        var customerDetail = Assert.IsType<CustomerDetailDto>(customerResult.Value);
        Assert.Equal(250m, customerDetail.TotalPaid);
        Assert.Equal(750m, customerDetail.Outstanding);

        var ledgerResponse = await new CustomersController(database).GetLedger(customer.Id, CancellationToken.None);
        var ledgerResult = Assert.IsType<OkObjectResult>(ledgerResponse.Result);
        var ledger = Assert.IsType<CustomerLedgerDto>(ledgerResult.Value);
        var receipt = Assert.Single(ledger.Transactions, transaction => transaction.Type == "Payment");
        Assert.Equal(new DateOnly(2026, 8, 29), receipt.Date);
        Assert.Equal(250m, receipt.Credit);
    }

    [Fact]
    public async Task UpdatePayment_WhenUnlinkedFromOrder_ResynchronizesThePreviousOrder()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-MOVE-PAYMENT",
            ContactName = "Move Payment Customer",
            Phone = "9876543210",
        };
        var order = new SalesOrder
        {
            OrderNumber = "MOVE-PAYMENT-ORDER",
            Customer = customer,
            Status = "Completed",
            OrderDate = new DateOnly(2026, 8, 27),
            GrandTotal = 500m,
        };
        var payment = new Payment
        {
            PaymentNumber = "PAY-MOVE",
            Customer = customer,
            SalesOrder = order,
            Amount = 500m,
            PaymentDate = new DateOnly(2026, 8, 27),
            Method = "UPI",
        };
        database.Payments.Add(payment);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var response = await new PaymentsController(database).UpdatePayment(
            payment.Id,
            new PaymentRequest
            {
                CustomerId = customer.Id,
                Amount = 500m,
                PaymentDate = new DateOnly(2026, 8, 29),
                Method = "UPI",
            },
            CancellationToken.None);

        var updated = Assert.IsType<OkObjectResult>(response.Result);
        var updatedPayment = Assert.IsType<PaymentDto>(updated.Value);
        Assert.Null(updatedPayment.SalesOrderId);

        database.ChangeTracker.Clear();
        var persistedOrder = await database.SalesOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal("Pending", persistedOrder.Status);
    }

    [Fact]
    public async Task DeletePayment_ExcludesTheTrackedDeletedReceiptWhenResynchronizingOrderStatus()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-DELETE-PAYMENT",
            ContactName = "Delete Payment Customer",
            Phone = "9876543210",
        };
        var order = new SalesOrder
        {
            OrderNumber = "DELETE-PAYMENT-ORDER",
            Customer = customer,
            Status = "Completed",
            OrderDate = new DateOnly(2026, 8, 27),
            GrandTotal = 500m,
        };
        var payment = new Payment
        {
            PaymentNumber = "PAY-DELETE",
            Customer = customer,
            SalesOrder = order,
            Amount = 500m,
            PaymentDate = new DateOnly(2026, 8, 27),
            Method = "UPI",
        };
        database.Payments.Add(payment);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var response = await new PaymentsController(database).DeletePayment(payment.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        database.ChangeTracker.Clear();
        Assert.False(await database.Payments.AnyAsync(item => item.Id == payment.Id));
        var persistedOrder = await database.SalesOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal("Pending", persistedOrder.Status);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
