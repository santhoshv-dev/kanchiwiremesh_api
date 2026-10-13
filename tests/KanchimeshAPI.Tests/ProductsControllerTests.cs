using System.ComponentModel.DataAnnotations;
using System.Globalization;
using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class ProductsControllerTests
{
    [Theory]
    [InlineData("1970x1550x26x8")]
    [InlineData("1970 × 1550 × 26 × 8")]
    [InlineData("1970  X  1550 x 26 x 8")]
    public async Task Search_NormalizesDimensionsBeforePagination(string search)
    {
        await using var database = CreateDatabase();
        for (var i = 0; i < 55; i++)
        {
            database.Products.Add(new Product { ProductCode = $"P{i}", Name = $"000 Product {i}", Category = "Mesh", Unit = "PCS" });
        }
        database.Products.Add(new Product { ProductCode = "TARGET", Name = "1970 x 1550 x 26 x 8 mm", Category = "Mesh", Unit = "PCS" });
        await database.SaveChangesAsync();
        var response = await new ProductsController(database).GetProducts(search, null);
        var page = Assert.IsType<PagedResult<ProductDto>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal("TARGET", Assert.Single(page.Items).ProductCode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0", true)]
    [InlineData("0.001", true)]
    [InlineData("230", true)]
    [InlineData("999999999999999", true)]
    [InlineData("-0.001", false)]
    [InlineData("1000000000000000", false)]
    public void ProductRequest_ValidatesDimensionBounds(string? value, bool expectedValid)
    {
        decimal? dimension = value is null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
        foreach (var propertyName in new[] { nameof(ProductRequest.Width), nameof(ProductRequest.Length) })
        {
            var context = new ValidationContext(new ProductRequest()) { MemberName = propertyName };
            var results = new List<ValidationResult>();

            Assert.Equal(expectedValid, Validator.TryValidateProperty(dimension, context, results));
        }
    }

    [Fact]
    public async Task CreateAndUpdateProduct_AcceptsZeroLength()
    {
        await using var database = CreateDatabase();
        var controller = new ProductsController(database);
        var request = new ProductRequest
        {
            Name = "230 mm Carry Roller",
            Category = "Carry Roller",
            HsnSac = "73114",
            Width = 230m,
            Length = 0m,
            Rate = 100m,
            Unit = "PCS",
            Description = "2",
            RawMaterials = [],
        };
        var validationResults = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), validationResults, true));

        var createdResponse = await controller.CreateProduct(request, CancellationToken.None);
        var created = Assert.IsType<ProductDto>(Assert.IsType<CreatedAtActionResult>(createdResponse.Result).Value);
        Assert.Equal(0m, created.Length);
        Assert.Equal(230m, created.Width);

        var updatedResponse = await controller.UpdateProduct(created.Id, request, CancellationToken.None);
        var updated = Assert.IsType<ProductDto>(Assert.IsType<OkObjectResult>(updatedResponse.Result).Value);
        Assert.Equal(0m, updated.Length);
        var persisted = await database.Products.AsNoTracking().SingleAsync();
        Assert.Equal(0m, persisted.Length);
        Assert.Equal(230m, persisted.Width);
    }

    [Fact]
    public async Task UpdateProduct_AddsAndReturnsANewRawMaterialRequirement()
    {
        await using var database = CreateDatabase();
        var product = new Product
        {
            ProductCode = "PRD-TEST",
            Name = "belt fastner",
            HsnSac = "9410",
            Category = "belt",
            Unit = "pcs",
            Rate = 350m,
        };
        var rawMaterial = new RawMaterial
        {
            Name = "wire",
            Unit = "kg",
            TotalStock = 100m,
        };
        database.AddRange(product, rawMaterial);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var controller = new ProductsController(database);
        var response = await controller.UpdateProduct(
            product.Id,
            new ProductRequest
            {
                Name = product.Name,
                HsnSac = product.HsnSac,
                Category = product.Category,
                Unit = product.Unit,
                Rate = product.Rate,
                RawMaterials =
                [
                    new ProductRawMaterialRequest
                    {
                        RawMaterialId = rawMaterial.Id,
                        RawMaterialName = rawMaterial.Name,
                        ConsumptionQuantity = 2m,
                    },
                ],
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var saved = Assert.IsType<ProductDto>(ok.Value);
        var persisted = await database.ProductRawMaterials.AsNoTracking().SingleAsync();
        Assert.Equal(product.Id, persisted.ProductId);
        Assert.Equal(rawMaterial.Id, persisted.RawMaterialId);

        var requirement = Assert.Single(saved.RawMaterials!);
        Assert.Equal(rawMaterial.Id, requirement.RawMaterialId);
        Assert.Equal(2m, requirement.ConsumptionQuantity);
    }

    [Fact]
    public async Task GetAllProductsStockHistory_FiltersByMonthCategoryAndProduct()
    {
        await using var database = CreateDatabase();
        var prod1 = new Product { ProductCode = "P1", Name = "Mesh Wire", Category = "Mesh", Unit = "PCS" };
        var prod2 = new Product { ProductCode = "P2", Name = "Roller", Category = "Roller", Unit = "PCS" };
        database.Products.AddRange(prod1, prod2);
        await database.SaveChangesAsync();

        database.ProductTransactions.AddRange(
            new ProductTransaction
            {
                ProductId = prod1.Id,
                TransactionType = "Purchase",
                TransactionDate = new DateOnly(2026, 10, 5),
                Quantity = 50,
                Amount = 5000,
            },
            new ProductTransaction
            {
                ProductId = prod2.Id,
                TransactionType = "Purchase",
                TransactionDate = new DateOnly(2026, 10, 8),
                Quantity = 20,
                Amount = 4000,
            },
            new ProductTransaction
            {
                ProductId = prod1.Id,
                TransactionType = "Adjustment",
                TransactionDate = new DateOnly(2026, 9, 15),
                Quantity = 10,
                Amount = 1000,
            }
        );
        await database.SaveChangesAsync();

        var controller = new ProductsController(database);

        // 1. All products
        var allRes = await controller.GetAllProductsStockHistory();
        var allPage = Assert.IsType<PagedResult<ProductTransactionDto>>(Assert.IsType<OkObjectResult>(allRes.Result).Value);
        Assert.Equal(3, allPage.TotalCount);

        // 2. Filter by month & year (October 2026)
        var octRes = await controller.GetAllProductsStockHistory(year: 2026, month: 10);
        var octPage = Assert.IsType<PagedResult<ProductTransactionDto>>(Assert.IsType<OkObjectResult>(octRes.Result).Value);
        Assert.Equal(2, octPage.TotalCount);

        // 3. Filter by category (Mesh)
        var meshRes = await controller.GetAllProductsStockHistory(category: "Mesh");
        var meshPage = Assert.IsType<PagedResult<ProductTransactionDto>>(Assert.IsType<OkObjectResult>(meshRes.Result).Value);
        Assert.Equal(2, meshPage.TotalCount);

        // 4. Filter by specific product (prod2)
        var p2Res = await controller.GetAllProductsStockHistory(productId: prod2.Id);
        var p2Page = Assert.IsType<PagedResult<ProductTransactionDto>>(Assert.IsType<OkObjectResult>(p2Res.Result).Value);
        Assert.Single(p2Page.Items);
        Assert.Equal(prod2.Id, p2Page.Items[0].ProductId);
    }

    [Fact]
    public async Task GetStockBalance_CalculatesOpeningStockAccuratelyAcrossMonthsAndYears()
    {
        await using var database = CreateDatabase();
        var prod1 = new Product { ProductCode = "P1", Name = "Mesh Wire", Category = "Mesh", Unit = "PCS", QuantityOnHand = 60m };
        var prod2 = new Product { ProductCode = "P2", Name = "Roller", Category = "Roller", Unit = "PCS", QuantityOnHand = 40m };
        database.Products.AddRange(prod1, prod2);
        await database.SaveChangesAsync();

        // Movements:
        // Sep 2026: prod1 Sale 10
        // Oct 2026: prod1 Purchase 20, prod2 Sale 5
        // Nov 2026: prod2 Purchase 10
        database.ProductTransactions.AddRange(
            new ProductTransaction
            {
                ProductId = prod1.Id,
                TransactionType = "Sale",
                TransactionDate = new DateOnly(2026, 9, 15),
                Quantity = 10,
                Amount = 1000,
            },
            new ProductTransaction
            {
                ProductId = prod1.Id,
                TransactionType = "Purchase",
                TransactionDate = new DateOnly(2026, 10, 5),
                Quantity = 20,
                Amount = 2000,
            },
            new ProductTransaction
            {
                ProductId = prod2.Id,
                TransactionType = "Sale",
                TransactionDate = new DateOnly(2026, 10, 12),
                Quantity = 5,
                Amount = 1000,
            },
            new ProductTransaction
            {
                ProductId = prod2.Id,
                TransactionType = "Purchase",
                TransactionDate = new DateOnly(2026, 11, 1),
                Quantity = 10,
                Amount = 2000,
            }
        );
        await database.SaveChangesAsync();

        var controller = new ProductsController(database);

        // 1. Opening balance at start of October 2026 (2026-10-01)
        var octBalanceRes = await controller.GetStockBalance(beforeDate: new DateOnly(2026, 10, 1));
        var octBalances = Assert.IsType<List<ProductStockBalanceDto>>(Assert.IsType<OkObjectResult>(octBalanceRes.Result).Value);
        var octProd1 = octBalances.Single(b => b.ProductId == prod1.Id);
        var octProd2 = octBalances.Single(b => b.ProductId == prod2.Id);

        // prod1: QuantityOnHand (60) - [Purchase Oct(20)] = 40
        Assert.Equal(40m, octProd1.OpeningStock);
        // prod2: QuantityOnHand (40) - [Sale Oct(-5) + Purchase Nov(10)] = 40 - 5 = 35
        Assert.Equal(35m, octProd2.OpeningStock);

        // Total opening balance for all products at start of Oct: 40 + 35 = 75
        var octTotalOpening = octBalances.Sum(b => b.OpeningStock);
        Assert.Equal(75m, octTotalOpening);

        // October changes:
        // prod1: +20 Added, 0 Sold -> Closing = 40 + 20 = 60
        // prod2: 0 Added, 5 Sold -> Closing = 35 - 5 = 30
        // Total October Closing = 60 + 30 = 90

        // 2. Opening balance at start of November 2026 (2026-11-01)
        var novBalanceRes = await controller.GetStockBalance(beforeDate: new DateOnly(2026, 11, 1));
        var novBalances = Assert.IsType<List<ProductStockBalanceDto>>(Assert.IsType<OkObjectResult>(novBalanceRes.Result).Value);
        var novProd1 = novBalances.Single(b => b.ProductId == prod1.Id);
        var novProd2 = novBalances.Single(b => b.ProductId == prod2.Id);

        // November Opening MUST EQUAL October Closing!
        Assert.Equal(60m, novProd1.OpeningStock);
        Assert.Equal(30m, novProd2.OpeningStock);

        // Total November Opening (90) MUST EQUAL Total October Closing (90)!
        var novTotalOpening = novBalances.Sum(b => b.OpeningStock);
        Assert.Equal(90m, novTotalOpening);
    }

    [Fact]
    public async Task GetStockBalance_OpeningBalanceConsistentAcrossYearBoundaryAndDateRanges()
    {
        await using var database = CreateDatabase();
        var prod = new Product { ProductCode = "P1", Name = "Wire Mesh", Category = "Mesh", Unit = "PCS", QuantityOnHand = 100m };
        database.Products.Add(prod);
        await database.SaveChangesAsync();

        // Timeline:
        // Nov 2025: Purchase 30
        // Dec 2025: Sale 10, Purchase 15
        // Jan 2026: Sale 20
        // Feb 2026: Purchase 25
        // Mar 2026: Sale 5
        database.ProductTransactions.AddRange(
            new ProductTransaction { ProductId = prod.Id, TransactionType = "Purchase", TransactionDate = new DateOnly(2025, 11, 10), Quantity = 30, Amount = 3000 },
            new ProductTransaction { ProductId = prod.Id, TransactionType = "Sale", TransactionDate = new DateOnly(2025, 12, 5), Quantity = 10, Amount = 1000 },
            new ProductTransaction { ProductId = prod.Id, TransactionType = "Purchase", TransactionDate = new DateOnly(2025, 12, 20), Quantity = 15, Amount = 1500 },
            new ProductTransaction { ProductId = prod.Id, TransactionType = "Sale", TransactionDate = new DateOnly(2026, 1, 15), Quantity = 20, Amount = 2000 },
            new ProductTransaction { ProductId = prod.Id, TransactionType = "Purchase", TransactionDate = new DateOnly(2026, 2, 10), Quantity = 25, Amount = 2500 },
            new ProductTransaction { ProductId = prod.Id, TransactionType = "Sale", TransactionDate = new DateOnly(2026, 3, 1), Quantity = 5, Amount = 500 }
        );
        await database.SaveChangesAsync();

        var controller = new ProductsController(database);

        // Current stock: 100
        // Working backwards from QuantityOnHand (100):
        //   Before Mar 1:  movements after = Sale Mar(-5) → net = -5 → opening = 100 - (-5) = 105
        //   Before Feb 1:  movements after = Purchase Feb(+25), Sale Mar(-5) → net = 20 → opening = 100 - 20 = 80
        //   Before Jan 1:  movements after = Sale Jan(-20), Purchase Feb(+25), Sale Mar(-5) → net = 0 → opening = 100 - 0 = 100
        //   Before Dec 1:  movements after = Sale Dec(-10), Purchase Dec(+15), Sale Jan(-20), Purchase Feb(+25), Sale Mar(-5) → net = 5 → opening = 100 - 5 = 95
        //   Before Nov 1:  movements after = Purchase Nov(+30), Sale Dec(-10), Purchase Dec(+15), Sale Jan(-20), Purchase Feb(+25), Sale Mar(-5) → net = 35 → opening = 100 - 35 = 65

        // 1. November 2025 opening
        var nov25 = await GetOpeningStock(controller, new DateOnly(2025, 11, 1));
        Assert.Equal(65m, nov25);

        // Nov closing = 65 + 30 = 95
        // 2. December 2025 opening should equal November closing (95)
        var dec25 = await GetOpeningStock(controller, new DateOnly(2025, 12, 1));
        Assert.Equal(95m, dec25);

        // Dec closing = 95 - 10 + 15 = 100
        // 3. January 2026 opening should equal December closing (100) — YEAR BOUNDARY
        var jan26 = await GetOpeningStock(controller, new DateOnly(2026, 1, 1));
        Assert.Equal(100m, jan26);

        // Jan closing = 100 - 20 = 80
        // 4. February 2026 opening should equal January closing (80)
        var feb26 = await GetOpeningStock(controller, new DateOnly(2026, 2, 1));
        Assert.Equal(80m, feb26);

        // Feb closing = 80 + 25 = 105
        // 5. March 2026 opening should equal February closing (105)
        var mar26 = await GetOpeningStock(controller, new DateOnly(2026, 3, 1));
        Assert.Equal(105m, mar26);

        // 6. Custom mid-month date: opening at Dec 15
        //    Movements after Dec 15: Purchase Dec 20 (+15), Sale Jan(-20), Purchase Feb(+25), Sale Mar(-5) → net = 15
        //    Opening = 100 - 15 = 85
        var midDec = await GetOpeningStock(controller, new DateOnly(2025, 12, 15));
        Assert.Equal(85m, midDec);

        // 7. Year start 2026 via yearly filter: opening at Jan 1 2026 = 100
        var yearStart = await GetOpeningStock(controller, new DateOnly(2026, 1, 1));
        Assert.Equal(100m, yearStart);

        // 8. Verify continuity: opening at any date = previous period's closing
        //    All-time from start: 65 + 30 - 10 + 15 - 20 + 25 - 5 = 100 (matches QuantityOnHand ✓)
        Assert.Equal(100m, prod.QuantityOnHand);
    }

    private static async Task<decimal> GetOpeningStock(ProductsController controller, DateOnly beforeDate)
    {
        var result = await controller.GetStockBalance(beforeDate: beforeDate);
        var balances = Assert.IsType<List<ProductStockBalanceDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        return balances.Sum(b => b.OpeningStock);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
