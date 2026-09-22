using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class ExpensesAndTransactionsTests
{
    [Fact]
    public async Task ExpenseCrud_WorksProperly()
    {
        await using var database = CreateDatabase();
        var controller = new ExpensesController(database);

        var createResult = await controller.CreateExpense(new ExpenseRequest
        {
            ExpenseDate = new DateOnly(2026, 9, 22),
            Category = "Rent",
            Description = "Factory rent for September",
            Amount = 15000m,
            PaymentMode = "Bank Transfer",
            PaidTo = "Landlord",
        }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var expense = Assert.IsType<ExpenseDto>(created.Value);
        Assert.StartsWith("EXP-", expense.ExpenseNumber);
        Assert.Equal(15000m, expense.Amount);
        Assert.Equal("Rent", expense.Category);

        var listResult = await controller.GetExpenses(null, null, null, null, 1, 10, CancellationToken.None);
        var okList = Assert.IsType<OkObjectResult>(listResult.Result);
        var paged = Assert.IsType<PagedResult<ExpenseDto>>(okList.Value);
        Assert.Single(paged.Items);

        var updateResult = await controller.UpdateExpense(expense.Id, new ExpenseRequest
        {
            ExpenseDate = new DateOnly(2026, 9, 22),
            Category = "Rent",
            Description = "Factory rent for September (Revised)",
            Amount = 16000m,
            PaymentMode = "Bank Transfer",
            PaidTo = "Landlord",
        }, CancellationToken.None);

        var updatedOk = Assert.IsType<OkObjectResult>(updateResult.Result);
        var updated = Assert.IsType<ExpenseDto>(updatedOk.Value);
        Assert.Equal(16000m, updated.Amount);

        var deleteResult = await controller.DeleteExpense(expense.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(deleteResult);
        Assert.Empty(await database.Expenses.ToListAsync());
    }

    [Fact]
    public async Task PurchasePartialPaymentFlow_CalculatesPendingAndStatusTransitions()
    {
        await using var database = CreateDatabase();
        var controller = new PurchasesController(database);

        // Step 1: Create purchase of ₹50,000 with initial payment of ₹20,000
        var createResult = await controller.CreatePurchase(new PurchaseRecordRequest
        {
            ProductName = "GI Wire 10 gauge",
            PurchaseDate = new DateOnly(2026, 9, 22),
            QuantityPurchased = 100m,
            UnitPrice = 500m,
            PurchaseAmount = 50000m,
            SupplierName = "Tata Steel",
            InitialPaidAmount = 20000m,
            PaymentMode = "UPI",
            PaymentReference = "UPI-REF-001",
        }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
        var purchase = Assert.IsType<PurchaseRecordDto>(created.Value);
        Assert.Equal(50000m, purchase.PurchaseAmount);
        Assert.Equal(20000m, purchase.TotalPaid);
        Assert.Equal(30000m, purchase.PendingAmount);
        Assert.Equal("Partially Paid", purchase.PaymentStatus);
        Assert.Single(purchase.Payments);
        database.ChangeTracker.Clear();

        // Step 2: Record additional payment of ₹10,000
        var payResult1 = await controller.RecordPayment(purchase.Id, new RecordPurchasePaymentRequest
        {
            Amount = 10000m,
            PaymentDate = new DateOnly(2026, 9, 25),
            PaymentMode = "Cash",
            Notes = "Second installment",
        }, CancellationToken.None);

        var okPay1 = Assert.IsType<OkObjectResult>(payResult1.Result);
        var afterPay1 = Assert.IsType<PurchaseRecordDto>(okPay1.Value);
        Assert.Equal(30000m, afterPay1.TotalPaid);
        Assert.Equal(20000m, afterPay1.PendingAmount);
        Assert.Equal("Partially Paid", afterPay1.PaymentStatus);
        Assert.Equal(2, afterPay1.Payments.Count);
        database.ChangeTracker.Clear();

        // Step 3: Record payment that exceeds pending amount -> should fail
        var overpayResult = await controller.RecordPayment(purchase.Id, new RecordPurchasePaymentRequest
        {
            Amount = 25000m, // Pending is 20,000!
            PaymentDate = new DateOnly(2026, 9, 26),
            PaymentMode = "Cash",
        }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(overpayResult.Result);
        database.ChangeTracker.Clear();

        // Step 4: Record remaining ₹20,000 -> status transitions to Fully Paid
        var payResult2 = await controller.RecordPayment(purchase.Id, new RecordPurchasePaymentRequest
        {
            Amount = 20000m,
            PaymentDate = new DateOnly(2026, 9, 28),
            PaymentMode = "Bank Transfer",
        }, CancellationToken.None);

        var okPay2 = Assert.IsType<OkObjectResult>(payResult2.Result);
        var afterPay2 = Assert.IsType<PurchaseRecordDto>(okPay2.Value);
        Assert.Equal(50000m, afterPay2.TotalPaid);
        Assert.Equal(0m, afterPay2.PendingAmount);
        Assert.Equal("Fully Paid", afterPay2.PaymentStatus);
        Assert.Equal(3, afterPay2.Payments.Count);
        database.ChangeTracker.Clear();

        // Step 5: Delete a payment -> status updates back to Partially Paid
        var paymentToDelete = afterPay2.Payments.Last();
        var deletePaymentResult = await controller.DeletePayment(purchase.Id, paymentToDelete.Id, CancellationToken.None);
        var okDelete = Assert.IsType<OkObjectResult>(deletePaymentResult.Result);
        var afterDelete = Assert.IsType<PurchaseRecordDto>(okDelete.Value);
        Assert.Equal(30000m, afterDelete.TotalPaid);
        Assert.Equal(20000m, afterDelete.PendingAmount);
        Assert.Equal("Partially Paid", afterDelete.PaymentStatus);
    }

    [Fact]
    public async Task TransactionsSummaryAndReport_UnifiesCashFlowWithoutDoubleCounting()
    {
        await using var database = CreateDatabase();

        // 1. Customer Payment (Inflow): ₹1,00,000
        var customer = new Customer
        {
            CustomerCode = "CUS-TEST-01",
            ContactName = "John Doe",
            Phone = "9876543210",
        };
        database.Customers.Add(customer);

        var custPayment = new Payment
        {
            PaymentNumber = "PAY-001",
            CustomerId = customer.Id,
            Amount = 100000m,
            PaymentDate = new DateOnly(2026, 9, 20),
            Method = "Bank Transfer",
        };
        database.Payments.Add(custPayment);

        // 2. Operational Expense (Outflow): ₹15,000
        var expense = new Expense
        {
            ExpenseNumber = "EXP-001",
            Category = "Electricity",
            Description = "Factory Power Bill",
            Amount = 15000m,
            ExpenseDate = new DateOnly(2026, 9, 21),
            PaymentMode = "UPI",
        };
        database.Expenses.Add(expense);

        // 3. Purchase Bill of ₹50,000 with Supplier Payment of ₹20,000
        var purchase = new PurchaseRecord
        {
            PurchaseNumber = "PUR-001",
            ProductName = "Steel Mesh",
            QuantityPurchased = 10m,
            PurchaseAmount = 50000m,
            SupplierName = "Steel Corp",
            PurchaseDate = new DateOnly(2026, 9, 22),
            PaymentStatus = "Partially Paid",
        };
        database.PurchaseRecords.Add(purchase);

        var supplierPayment = new PurchasePayment
        {
            PaymentNumber = "PPAY-001",
            PurchaseRecordId = purchase.Id,
            Amount = 20000m,
            PaymentDate = new DateOnly(2026, 9, 22),
            PaymentMode = "UPI",
        };
        database.PurchasePayments.Add(supplierPayment);

        await database.SaveChangesAsync();

        var transactionsController = new TransactionsController(database);

        // Test Summary:
        // Incoming = 100,000
        // General Expenses = 15,000
        // Supplier Payments = 20,000
        // Total Outgoing = 35,000
        // Net Balance = 100,000 - 35,000 = 65,000
        var summaryResult = await transactionsController.GetSummary(null, null, CancellationToken.None);
        var okSummary = Assert.IsType<OkObjectResult>(summaryResult.Result);
        var summary = Assert.IsType<TransactionSummaryDto>(okSummary.Value);

        Assert.Equal(100000m, summary.TotalIncoming);
        Assert.Equal(15000m, summary.TotalGeneralExpenses);
        Assert.Equal(20000m, summary.TotalSupplierPayments);
        Assert.Equal(35000m, summary.TotalOutgoing);
        Assert.Equal(65000m, summary.NetBalance);

        // Test Report:
        var reportResult = await transactionsController.GetReport(null, null, null, null, CancellationToken.None);
        var okReport = Assert.IsType<OkObjectResult>(reportResult.Result);
        var report = Assert.IsType<TransactionReportDto>(okReport.Value);

        Assert.Equal(3, report.TotalCount);
        Assert.Equal(100000m, report.TotalIncoming);
        Assert.Equal(35000m, report.TotalOutgoing);
        Assert.Equal(65000m, report.NetBalance);

        // Verify running balance:
        // Item 1 (Sep 20): +100,000 -> Running = 100,000
        // Item 2 (Sep 21): -15,000  -> Running = 85,000
        // Item 3 (Sep 22): -20,000  -> Running = 65,000
        Assert.Equal(100000m, report.Items[0].RunningBalance);
        Assert.Equal(85000m, report.Items[1].RunningBalance);
        Assert.Equal(65000m, report.Items[2].RunningBalance);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
