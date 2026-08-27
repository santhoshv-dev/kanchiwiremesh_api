using KanchimeshAPI.Controllers;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Tests;

public sealed class OrdersControllerTests
{
    [Fact]
    public async Task CreateOrder_CreatesAndReturnsTheOrder()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-CREATE",
            ContactName = "Create Customer",
            Phone = "9876543210",
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);

        var response = await controller.CreateOrder(
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = new DateOnly(2026, 8, 26),
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        Description = "Wire mesh",
                        Quantity = 2m,
                        Unit = "pcs",
                        Rate = 100m,
                        IgstRate = 18m,
                    },
                ],
            },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        var detail = Assert.IsType<OrderDetailDto>(created.Value);
        Assert.Equal("1", detail.OrderNumber);
        Assert.Equal(236m, detail.GrandTotal);
        var persisted = await database.SalesOrders.AsNoTracking().SingleAsync();
        Assert.Equal(customer.Id, persisted.CustomerId);
    }

    [Fact]
    public async Task CreateOrder_ReturnsBadRequestForAQuantityAndRateThatWouldOverflow()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-OVERFLOW",
            ContactName = "Overflow Customer",
            Phone = "9876543210",
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);

        var response = await controller.CreateOrder(
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = new DateOnly(2026, 8, 27),
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        Description = "Oversized wire mesh",
                        Quantity = 999_999_999_999_999m,
                        Unit = "pcs",
                        Rate = 999_999_999_999_999m,
                        IgstRate = 18m,
                    },
                ],
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(OrderRequest.Items), problem.Errors.Keys);
        Assert.Empty(await database.SalesOrders.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task UpdateOrder_ReturnsBadRequestForAQuantityAndRateThatWouldOverflow()
    {
        await using var database = CreateDatabase();
        var order = await SeedPaidOrder(database);
        var controller = new OrdersController(database);

        var response = await controller.UpdateOrder(
            order.Id,
            new OrderRequest
            {
                CustomerId = order.CustomerId,
                OrderDate = order.OrderDate,
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        Description = "Oversized wire mesh",
                        Quantity = 999_999_999_999_999m,
                        Unit = "pcs",
                        Rate = 999_999_999_999_999m,
                    },
                ],
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(OrderRequest.Items), problem.Errors.Keys);
        database.ChangeTracker.Clear();
        var persisted = await database.SalesOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal(1180m, persisted.GrandTotal);
    }

    [Fact]
    public async Task CreateOrder_ReturnsConflictWhenNumericOrderNumbersAreExhausted()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-SEQUENCE",
            ContactName = "Sequence Customer",
            Phone = "9876543210",
        };
        database.SalesOrders.Add(new SalesOrder
        {
            OrderNumber = int.MaxValue.ToString(),
            Customer = customer,
            Status = "Pending",
            OrderDate = new DateOnly(2026, 8, 27),
        });
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);

        var response = await controller.CreateOrder(
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = new DateOnly(2026, 8, 27),
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        Description = "Wire mesh",
                        Quantity = 1m,
                        Unit = "pcs",
                        Rate = 100m,
                    },
                ],
            },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public async Task FullUpdate_CannotCancelAnOrderWithRecordedPayments()
    {
        await using var database = CreateDatabase();
        var order = await SeedPaidOrder(database);
        var controller = new OrdersController(database);
        var request = CreateCancellationRequest(order);

        var response = await controller.UpdateOrder(order.Id, request, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        var persisted = await database.SalesOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal("Pending", persisted.Status);
    }

    [Fact]
    public async Task StatusUpdate_CannotCancelAnOrderWithRecordedPayments()
    {
        await using var database = CreateDatabase();
        var order = await SeedPaidOrder(database);
        var controller = new OrdersController(database);

        var response = await controller.UpdateStatus(
            order.Id,
            new OrderStatusRequest { Status = "Cancelled" },
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(response.Result);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(409, problem.Status);
        var persisted = await database.SalesOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.Equal("Pending", persisted.Status);
    }

    private static KanchimeshDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new KanchimeshDbContext(options);
    }

    private static async Task<SalesOrder> SeedPaidOrder(KanchimeshDbContext database)
    {
        var customer = new Customer
        {
            CustomerCode = "CUS-TEST",
            ContactName = "Test Customer",
            Phone = "9876543210",
        };
        var order = new SalesOrder
        {
            OrderNumber = "ERH1",
            Customer = customer,
            Status = "Pending",
            OrderDate = new DateOnly(2026, 8, 24),
            Subtotal = 1000m,
            TaxAmount = 180m,
            GrandTotal = 1180m,
            Items =
            [
                new SalesOrderItem
                {
                    Description = "Wire mesh",
                    Quantity = 1m,
                    Unit = "pcs",
                    Rate = 1000m,
                    IgstRate = 18m, SgstRate = 0m, CgstRate = 0m,
                    LineSubtotal = 1000m,
                    TaxAmount = 180m,
                    LineTotal = 1180m,
                },
            ],
        };
        order.Payments.Add(new Payment
        {
            PaymentNumber = "PAY-TEST",
            Customer = customer,
            SalesOrder = order,
            Amount = 500m,
            Method = "UPI",
        });
        database.SalesOrders.Add(order);
        await database.SaveChangesAsync();
        return order;
    }

    private static OrderRequest CreateCancellationRequest(SalesOrder order) => new()
    {
        CustomerId = order.CustomerId,
        OrderDate = order.OrderDate,
        Status = "Cancelled",
        Items =
        [
            new OrderItemRequest
            {
                Description = "Wire mesh",
                Quantity = 1m,
                Unit = "pcs",
                Rate = 1000m,
                IgstRate = 18m, SgstRate = 0m, CgstRate = 0m,
            },
        ],
    };
}
