using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class BorrowsControllerTests
{
    [Fact]
    public async Task CreateBorrow_RequiresTheManuallyEnteredFields()
    {
        await using var database = CreateDatabase();
        var controller = new BorrowsController(database);

        var missingName = await controller.CreateBorrow(
            new BorrowRequest { Amount = 5000m, PaymentMode = "Cash" },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(missingName.Result);

        var missingAmount = await controller.CreateBorrow(
            new BorrowRequest { BorrowerName = "Ramesh", PaymentMode = "Cash" },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(missingAmount.Result);

        var missingMethod = await controller.CreateBorrow(
            new BorrowRequest { BorrowerName = "Ramesh", Amount = 5000m, PaymentMode = "   " },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(missingMethod.Result);

        Assert.Empty(database.Borrows);
    }

    [Fact]
    public async Task CreateBorrow_SavesTheManualEntryAndStartsFullyOutstanding()
    {
        await using var database = CreateDatabase();
        var controller = new BorrowsController(database);

        var response = await controller.CreateBorrow(
            new BorrowRequest
            {
                BorrowerName = "  Ramesh Babu  ",
                BorrowDate = new DateOnly(2026, 10, 1),
                Amount = 25000m,
                PaymentMode = "  Bank Transfer  ",
                Notes = "Machinery advance",
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var dto = Assert.IsType<BorrowDto>(created.Value);
        Assert.Equal("Ramesh Babu", dto.BorrowerName);
        Assert.Equal("Bank Transfer", dto.PaymentMode);
        Assert.Equal(25000m, dto.Amount);
        Assert.Equal(0m, dto.TotalRepaid);
        Assert.Equal(25000m, dto.OutstandingAmount);
        Assert.Equal("Pending", dto.Status);
        Assert.Equal("BRW-1", dto.BorrowNumber);

        var saved = Assert.Single(database.Borrows);
        Assert.Equal("Ramesh Babu", saved.BorrowerName);
    }

    [Fact]
    public async Task AddRepayment_TracksAmountDateMethodAndRejectsOverpayment()
    {
        await using var database = CreateDatabase();
        var controller = new BorrowsController(database);

        var created = await controller.CreateBorrow(
            new BorrowRequest { BorrowerName = "Lakshmi", Amount = 10000m, PaymentMode = "Cash" },
            CancellationToken.None);
        var borrow = Assert.IsType<BorrowDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);

        var tooMuch = await controller.AddRepayment(
            borrow.Id,
            new BorrowRepaymentRequest { Amount = 12000m, PaymentMode = "UPI" },
            CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(tooMuch.Result);

        var response = await controller.AddRepayment(
            borrow.Id,
            new BorrowRepaymentRequest
            {
                Amount = 4000m,
                PaymentDate = new DateOnly(2026, 10, 5),
                PaymentMode = "UPI",
                Notes = "First instalment",
            },
            CancellationToken.None);

        var updated = Assert.IsType<BorrowDto>(
            Assert.IsType<CreatedAtActionResult>(response.Result).Value);
        Assert.Equal(4000m, updated.TotalRepaid);
        Assert.Equal(6000m, updated.OutstandingAmount);
        Assert.Equal("Pending", updated.Status);
        var repayment = Assert.Single(updated.Repayments);
        Assert.Equal("UPI", repayment.PaymentMode);
        Assert.Equal(new DateOnly(2026, 10, 5), repayment.PaymentDate);

        await controller.AddRepayment(
            borrow.Id,
            new BorrowRepaymentRequest { Amount = 6000m, PaymentMode = "Cash" },
            CancellationToken.None);
        var settled = await controller.GetBorrow(borrow.Id, CancellationToken.None);
        var settledDto = Assert.IsType<BorrowDto>(
            Assert.IsType<OkObjectResult>(settled.Result).Value);
        Assert.Equal(0m, settledDto.OutstandingAmount);
        Assert.Equal("Repaid", settledDto.Status);
    }

    [Fact]
    public async Task GetSummary_ReportsOutstandingBorrowSeparatelyFromPayments()
    {
        await using var database = CreateDatabase();
        var controller = new BorrowsController(database);

        var created = await controller.CreateBorrow(
            new BorrowRequest { BorrowerName = "Ravi", Amount = 8000m, PaymentMode = "Cash" },
            CancellationToken.None);
        var borrow = Assert.IsType<BorrowDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        await controller.AddRepayment(
            borrow.Id,
            new BorrowRepaymentRequest { Amount = 3000m, PaymentMode = "Cash" },
            CancellationToken.None);

        var response = await controller.GetSummary(CancellationToken.None);
        var summary = Assert.IsType<BorrowSummaryDto>(
            Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(8000m, summary.TotalBorrowed);
        Assert.Equal(3000m, summary.TotalRepaid);
        Assert.Equal(5000m, summary.OutstandingAmount);
        Assert.Equal(1, summary.BorrowCount);
        Assert.Equal(1, summary.ActiveBorrowCount);
    }

    [Fact]
    public async Task GetDashboard_SurfacesTheOutstandingBorrowAmount()
    {
        await using var database = CreateDatabase();
        var controller = new BorrowsController(database);

        var created = await controller.CreateBorrow(
            new BorrowRequest { BorrowerName = "Suresh", Amount = 15000m, PaymentMode = "Cash" },
            CancellationToken.None);
        var borrow = Assert.IsType<BorrowDto>(
            Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        await controller.AddRepayment(
            borrow.Id,
            new BorrowRepaymentRequest { Amount = 5000m, PaymentMode = "Cash" },
            CancellationToken.None);

        var response = await new DashboardController(database)
            .GetDashboard(CancellationToken.None);
        var summary = Assert.IsType<DashboardSummaryDto>(
            Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(15000m, summary.TotalBorrowed);
        Assert.Equal(10000m, summary.OutstandingBorrow);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}

