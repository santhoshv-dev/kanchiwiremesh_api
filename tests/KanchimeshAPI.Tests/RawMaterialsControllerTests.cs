using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class RawMaterialsControllerTests
{
    [Fact]
    public async Task AddStock_PreservesLatestBalanceAndUpdatesProductCalculation()
    {
        await using var database = new KanchimeshDbContext(new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var material = new RawMaterial { Name = "Wire", TotalStock = 100m, UsedStock = 20m };
        database.RawMaterials.Add(material);
        await database.SaveChangesAsync();
        var controller = new RawMaterialsController(database);
        await controller.UpdateRawMaterial(material.Id, new RawMaterialRequest
        { Name = "Wire", Quantity = 100m, OriginalQuantity = 100m, AddStock = 50m }, default);
        Assert.Equal(150m, material.TotalStock);
        var product = new Product { Rate = 10m, RawMaterials = [new ProductRawMaterial
            { RawMaterial = material, ConsumptionQuantity = 2m }] };
        Assert.Equal(65m, product.ToDto().Pieces);
        Assert.Equal(650m, product.ToDto().TotalAmount);
        await controller.UpdateRawMaterial(material.Id, new RawMaterialRequest
        { Name = "Wire", Quantity = 100m, OriginalQuantity = 100m, AddStock = 10m }, default);
        Assert.Equal(160m, material.TotalStock);
        Assert.Equal(700m, product.ToDto().TotalAmount);
    }

    [Fact]
    public void Pieces_UseLimitingMaterialAndNeverBecomeNegative()
    {
        var limiting = new RawMaterial { TotalStock = 9m, UsedStock = 2m };
        var product = new Product { Rate = 20m, RawMaterials = [
            new ProductRawMaterial { RawMaterial = new RawMaterial { TotalStock = 100m }, ConsumptionQuantity = 2m },
            new ProductRawMaterial { RawMaterial = limiting, ConsumptionQuantity = 3m }] };
        Assert.Equal(2m, product.ToDto().Pieces);
        limiting.UsedStock = 10m;
        Assert.Equal(0m, product.ToDto().TotalAmount);
    }
}
