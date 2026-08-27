using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class CompanySettingsControllerTests
{
    [Fact]
    public async Task UpdateCompanyProfile_PersistsTheInvoiceAddressAndPaymentInstructions()
    {
        await using var database = CreateDatabase();
        var controller = new CompanySettingsController(database);

        var response = await controller.UpdateCompanyProfile(
            new CompanyProfileRequest
            {
                CompanyName = "  Kanchi Mesh  ",
                Address = "  No. 10, Industrial Estate\nChennai  ",
                City = "Chennai",
                State = "Tamil Nadu",
                PostalCode = "600001",
                Phone = "9876543210",
                Email = "accounts@example.test",
                GstNumber = "33ABCDE1234F1Z5",
                BankName = "Example Bank",
                BankAccountName = "Kanchi Mesh",
                BankAccountNumber = "1234567890",
                BankIfscCode = "exam0000123",
                BankBranch = "Chennai Main",
                UpiId = "kanchi@example",
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var saved = Assert.IsType<CompanyProfileDto>(ok.Value);
        Assert.Equal("Kanchi Mesh", saved.CompanyName);
        Assert.Equal("No. 10, Industrial Estate\nChennai", saved.Address);
        Assert.Equal("33ABCDE1234F1Z5", saved.GstNumber);
        Assert.Equal("EXAM0000123", saved.BankIfscCode);

        database.ChangeTracker.Clear();
        var getResponse = await controller.GetCompanyProfile(CancellationToken.None);
        var returned = Assert.IsType<OkObjectResult>(getResponse.Result);
        var profile = Assert.IsType<CompanyProfileDto>(returned.Value);
        Assert.Equal("Example Bank", profile.BankName);
        Assert.Equal("1234567890", profile.BankAccountNumber);
        Assert.Equal("kanchi@example", profile.UpiId);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
