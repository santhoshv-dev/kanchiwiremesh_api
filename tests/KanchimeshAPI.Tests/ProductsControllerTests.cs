using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class ProductsControllerTests
{
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

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
