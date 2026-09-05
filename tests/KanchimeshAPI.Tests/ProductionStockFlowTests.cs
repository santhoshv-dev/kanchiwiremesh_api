using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class ProductionStockFlowTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(0.25)]
    public async Task AddStockConsumesMaterials_OrderLifecycleOnlyMovesFinishedStock(decimal consumption)
    {
        await using var database = new KanchimeshDbContext(new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var material = new RawMaterial { Name = "Wire", TotalStock = 100m };
        var secondMaterial = new RawMaterial { Name = "Frame", TotalStock = 100m };
        var product = new Product
        {
            ProductCode = "P1", Name = "Mesh", Category = "Mesh", Unit = "pcs", Rate = 10m,
            RawMaterials = [
                new ProductRawMaterial { RawMaterial = material, ConsumptionQuantity = consumption },
                new ProductRawMaterial { RawMaterial = secondMaterial, ConsumptionQuantity = 1m }],
        };
        var customer = new Customer { CustomerCode = "C1", ContactName = "Customer", Phone = "9876543210" };
        database.AddRange(product, customer);
        await database.SaveChangesAsync();
        var products = new ProductsController(database);
        var orders = new OrdersController(database);

        foreach (var addition in new[] { 10m, 5m })
        {
            var response = await products.AdjustStock(product.Id,
                new StockAdjustmentRequest { QuantityChange = addition }, default);
            Assert.IsType<OkObjectResult>(response.Result);
            database.ChangeTracker.Clear();
            var stock = await database.Products.AsNoTracking().SingleAsync();
            var raw = await database.RawMaterials.AsNoTracking().SingleAsync(r => r.Id == material.Id);
            Assert.Equal(stock.TotalStockAdded * consumption, raw.UsedStock);
            Assert.Equal(100m - stock.TotalStockAdded * consumption, raw.TotalStock - raw.UsedStock);
        }
        Assert.Equal(15m, (await database.Products.AsNoTracking().SingleAsync()).QuantityOnHand);
        Assert.Equal(15m, (await database.RawMaterials.AsNoTracking().SingleAsync(r => r.Id == secondMaterial.Id)).UsedStock);
        Assert.Equal(2, await database.ProductTransactions.CountAsync());

        var created = await orders.CreateOrder(new OrderRequest
        {
            CustomerId = customer.Id, OrderDate = new DateOnly(2026, 9, 5), Status = "Pending",
            Items = [new OrderItemRequest { ProductId = product.Id, Description = "Mesh", Quantity = 4m, Unit = "pcs", Rate = 10m }],
        }, default);
        var order = Assert.IsType<OrderDetailDto>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        await AssertBalances(11m);
        Assert.IsType<OkObjectResult>((await orders.UpdateStatus(order.Id, new OrderStatusRequest { Status = "Cancelled" }, default)).Result);
        await AssertBalances(15m);
        Assert.IsType<OkObjectResult>((await orders.UpdateStatus(order.Id, new OrderStatusRequest { Status = "Pending" }, default)).Result);
        await AssertBalances(11m);
        Assert.IsType<NoContentResult>(await orders.DeleteOrder(order.Id, default));
        await AssertBalances(15m);

        async Task AssertBalances(decimal finishedStock)
        {
            database.ChangeTracker.Clear();
            Assert.Equal(finishedStock, (await database.Products.AsNoTracking().SingleAsync()).QuantityOnHand);
            var raw = await database.RawMaterials.AsNoTracking().SingleAsync(r => r.Id == material.Id);
            Assert.Equal(15m * consumption, raw.UsedStock);
            Assert.Equal(100m - 15m * consumption, raw.TotalStock - raw.UsedStock);
            Assert.Equal(15m, (await database.RawMaterials.AsNoTracking().SingleAsync(r => r.Id == secondMaterial.Id)).UsedStock);
        }
    }
}
