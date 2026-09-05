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

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }
}
