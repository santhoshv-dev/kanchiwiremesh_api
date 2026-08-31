using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class PurchasesControllerTests
{
    [Fact]
    public async Task CreatePurchase_SavesAManualRecordWithoutChangingProductStock()
    {
        await using var database = CreateDatabase();
        var product = new Product
        {
            ProductCode = "PRD-UNCHANGED",
            Name = "Manufactured mesh",
            Category = "Mesh",
            Unit = "pcs",
            QuantityOnHand = 12m,
            TotalStockAdded = 12m,
        };
        database.Products.Add(product);
        await database.SaveChangesAsync();

        var controller = new PurchasesController(database);
        var response = await controller.CreatePurchase(
            new PurchaseRecordRequest
            {
                ProductName = "Galvanized wire",
                PurchaseDate = new DateOnly(2026, 8, 31),
                QuantityPurchased = 25m,
                PurchaseAmount = 0m,
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var purchase = Assert.IsType<PurchaseRecordDto>(created.Value);
        Assert.StartsWith("PUR-", purchase.PurchaseNumber);
        Assert.Equal("Galvanized wire", purchase.ProductName);
        Assert.Equal("Pending", purchase.PaymentStatus);
        Assert.Null(purchase.SupplierName);
        Assert.Null(purchase.BuyerName);

        database.ChangeTracker.Clear();
        var savedPurchase = await database.PurchaseRecords.AsNoTracking().SingleAsync();
        var savedProduct = await database.Products.AsNoTracking().SingleAsync();
        Assert.Equal(purchase.Id, savedPurchase.Id);
        Assert.Equal(25m, savedPurchase.QuantityPurchased);
        Assert.Equal(0m, savedPurchase.PurchaseAmount);
        Assert.Equal(12m, savedProduct.QuantityOnHand);
        Assert.Equal(12m, savedProduct.TotalStockAdded);
    }

    [Fact]
    public async Task UpdateListAndDeletePurchase_UseTheStandalonePurchaseHistory()
    {
        await using var database = CreateDatabase();
        var controller = new PurchasesController(database);
        var created = await CreatePurchase(controller, "Stainless steel wire", "Acme Metals");

        var update = await controller.UpdatePurchase(
            created.Id,
            new PurchaseRecordRequest
            {
                ProductName = "Stainless steel wire",
                ProductCode = "RAW-SS-01",
                BuyerName = "Kanchi Wire Mesh",
                BuyerContactNumber = "9876543210",
                BuyerGstNumber = "33ABCDE1234F1Z5",
                BuyerLocation = "Chennai",
                SupplierName = "Acme Metals",
                PurchaseDate = new DateOnly(2026, 8, 31),
                QuantityPurchased = 10.125m,
                PurchaseAmount = 1200.25m,
                GstAmount = 216.75m,
                GstRate = 18.25m,
                PaymentStatus = "partial",
                Notes = "Invoice received",
            },
            CancellationToken.None);

        var updatedResult = Assert.IsType<OkObjectResult>(update.Result);
        var updated = Assert.IsType<PurchaseRecordDto>(updatedResult.Value);
        Assert.Equal("Partial", updated.PaymentStatus);
        Assert.Equal("RAW-SS-01", updated.ProductCode);
        Assert.Equal(18.25m, updated.GstRate);

        var list = await controller.GetPurchases(
            "acme",
            page: 1,
            pageSize: 50,
            cancellationToken: CancellationToken.None);
        var listResult = Assert.IsType<OkObjectResult>(list.Result);
        var page = Assert.IsType<PagedResult<PurchaseRecordDto>>(listResult.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal(updated.Id, item.Id);

        var delete = await controller.DeletePurchase(updated.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(delete);
        Assert.Empty(await database.PurchaseRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreatePurchase_RequiresMeaningfulCorePurchaseFields()
    {
        await using var database = CreateDatabase();
        var controller = new PurchasesController(database);

        var response = await controller.CreatePurchase(
            new PurchaseRecordRequest
            {
                ProductName = "  ",
                PurchaseDate = null,
                QuantityPurchased = 0m,
                PurchaseAmount = -1m,
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(PurchaseRecordRequest.ProductName), problem.Errors.Keys);
        Assert.Empty(await database.PurchaseRecords.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreatePurchase_RejectsValuesBeyondDatabaseDecimalScale()
    {
        await using var database = CreateDatabase();
        var controller = new PurchasesController(database);
        var invalidRequests = new (string Field, PurchaseRecordRequest Request)[]
        {
            (
                nameof(PurchaseRecordRequest.QuantityPurchased),
                new PurchaseRecordRequest
                {
                    ProductName = "Binding wire",
                    PurchaseDate = new DateOnly(2026, 8, 31),
                    QuantityPurchased = 1.0001m,
                    PurchaseAmount = 100m,
                }),
            (
                nameof(PurchaseRecordRequest.PurchaseAmount),
                new PurchaseRecordRequest
                {
                    ProductName = "Binding wire",
                    PurchaseDate = new DateOnly(2026, 8, 31),
                    QuantityPurchased = 1m,
                    PurchaseAmount = 100.001m,
                }),
            (
                nameof(PurchaseRecordRequest.GstAmount),
                new PurchaseRecordRequest
                {
                    ProductName = "Binding wire",
                    PurchaseDate = new DateOnly(2026, 8, 31),
                    QuantityPurchased = 1m,
                    PurchaseAmount = 100m,
                    GstAmount = 18.001m,
                }),
            (
                nameof(PurchaseRecordRequest.GstRate),
                new PurchaseRecordRequest
                {
                    ProductName = "Binding wire",
                    PurchaseDate = new DateOnly(2026, 8, 31),
                    QuantityPurchased = 1m,
                    PurchaseAmount = 100m,
                    GstRate = 18.001m,
                }),
        };

        foreach (var (field, request) in invalidRequests)
        {
            var response = await controller.CreatePurchase(request, CancellationToken.None);

            var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
            var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
            Assert.Contains(field, problem.Errors.Keys);
        }

        Assert.Empty(await database.PurchaseRecords.AsNoTracking().ToListAsync());
    }

    private static async Task<PurchaseRecordDto> CreatePurchase(
        PurchasesController controller,
        string productName,
        string supplierName)
    {
        var response = await controller.CreatePurchase(
            new PurchaseRecordRequest
            {
                ProductName = productName,
                SupplierName = supplierName,
                PurchaseDate = new DateOnly(2026, 8, 30),
                QuantityPurchased = 5m,
                PurchaseAmount = 500m,
                PaymentStatus = "Paid",
            },
            CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        return Assert.IsType<PurchaseRecordDto>(created.Value);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
