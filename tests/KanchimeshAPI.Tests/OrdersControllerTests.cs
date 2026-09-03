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
        Assert.Equal("01/26-27", detail.OrderNumber);
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
    public async Task CreateOrder_PreventsOrderingMoreThanAvailableStock()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-STOCK-CREATE",
            ContactName = "Stock Customer",
            Phone = "9876543210",
        };
        var product = new Product
        {
            ProductCode = "PRD-STOCK-CREATE",
            Name = "Available mesh",
            Category = "Mesh",
            Unit = "pcs",
            QuantityOnHand = 5m,
        };
        database.AddRange(customer, product);
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
                        ProductId = product.Id,
                        Description = product.Name,
                        Quantity = 6m,
                        Unit = "pcs",
                        Rate = 100m,
                    },
                ],
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(OrderRequest.Items), problem.Errors.Keys);
        Assert.Empty(await database.SalesOrders.AsNoTracking().ToListAsync());
        database.ChangeTracker.Clear();
        Assert.Equal(5m, (await database.Products.SingleAsync(item => item.Id == product.Id)).QuantityOnHand);
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
    public async Task CreateOrder_ReturnsConflictWhenFinancialYearInvoiceNumbersAreExhausted()
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
            OrderNumber = $"{int.MaxValue}/26-27",
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

    [Fact]
    public async Task CreateOrder_ResetsTheInvoiceSequenceForANewFinancialYear()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-FY",
            ContactName = "Financial Year Customer",
            Phone = "9876543210",
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);

        var first = await CreateSimpleOrder(controller, customer.Id, new DateOnly(2026, 4, 1));
        var sameFinancialYear = await CreateSimpleOrder(controller, customer.Id, new DateOnly(2027, 3, 31));
        var nextFinancialYear = await CreateSimpleOrder(controller, customer.Id, new DateOnly(2027, 4, 1));

        Assert.Equal("01/26-27", first.OrderNumber);
        Assert.Equal("02/26-27", sameFinancialYear.OrderNumber);
        Assert.Equal("01/27-28", nextFinancialYear.OrderNumber);
    }

    [Fact]
    public async Task UpdateOrder_ChangesAnExistingQuantityAndAppliesOnlyTheStockDifference()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-QUANTITY",
            ContactName = "Quantity Customer",
            Phone = "9876543210",
        };
        var product = new Product
        {
            ProductCode = "PRD-QUANTITY",
            Name = "Quantity mesh",
            Category = "Mesh",
            Unit = "pcs",
            QuantityOnHand = 100m,
        };
        var order = new SalesOrder
        {
            OrderNumber = "01/26-27",
            Customer = customer,
            OrderDate = new DateOnly(2026, 8, 27),
            Status = "Pending",
            Items =
            [
                new SalesOrderItem
                {
                    Product = product,
                    Description = product.Name,
                    Quantity = 20m,
                    Unit = "pcs",
                    Rate = 10m,
                },
            ],
        };
        OrderCalculator.Recalculate(order);
        database.SalesOrders.Add(order);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var controller = new OrdersController(database);
        var response = await controller.UpdateOrder(
            order.Id,
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = order.OrderDate,
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        ProductId = product.Id,
                        Description = product.Name,
                        Quantity = 30m,
                        Unit = "pcs",
                        Rate = 10m,
                    },
                ],
            },
            CancellationToken.None);

        var updated = Assert.IsType<OkObjectResult>(response.Result);
        var detail = Assert.IsType<OrderDetailDto>(updated.Value);
        Assert.Equal(30m, detail.Items.Single().Quantity);
        Assert.Equal(300m, detail.GrandTotal);

        database.ChangeTracker.Clear();
        var persistedOrder = await database.SalesOrders
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == order.Id);
        var persistedProduct = await database.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(30m, persistedOrder.Items.Single().Quantity);
        Assert.Equal(300m, persistedOrder.GrandTotal);
        Assert.Equal(70m, persistedProduct.QuantityOnHand);
        Assert.Equal(30m, persistedProduct.TotalSold);
    }

    [Fact]
    public async Task UpdateOrder_PreventsAnIncreaseBeyondAvailableStockWhileKeepingItsCurrentAllocation()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-STOCK-UPDATE",
            ContactName = "Stock Update Customer",
            Phone = "9876543210",
        };
        var product = new Product
        {
            ProductCode = "PRD-STOCK-UPDATE",
            Name = "Update mesh",
            Category = "Mesh",
            Unit = "pcs",
            QuantityOnHand = 5m,
        };
        database.AddRange(customer, product);
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);
        var created = await controller.CreateOrder(
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = new DateOnly(2026, 8, 27),
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        ProductId = product.Id,
                        Description = product.Name,
                        Quantity = 3m,
                        Unit = "pcs",
                        Rate = 100m,
                    },
                ],
            },
            CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(created.Result);
        var order = Assert.IsType<OrderDetailDto>(createdResult.Value);

        var response = await controller.UpdateOrder(
            order.Id,
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = order.OrderDate,
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        ProductId = product.Id,
                        Description = product.Name,
                        Quantity = 6m,
                        Unit = "pcs",
                        Rate = 100m,
                    },
                ],
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(OrderRequest.Items), problem.Errors.Keys);
        database.ChangeTracker.Clear();
        var persistedOrder = await database.SalesOrders.Include(item => item.Items).SingleAsync(item => item.Id == order.Id);
        var persistedProduct = await database.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(3m, persistedOrder.Items.Single().Quantity);
        Assert.Equal(2m, persistedProduct.QuantityOnHand);
    }

    [Fact]
    public async Task UpdateOrder_ChangesAManualLineQuantityWithoutTryingToAdjustStock()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-MANUAL-QUANTITY",
            ContactName = "Manual Quantity Customer",
            Phone = "9876543210",
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);
        var created = await CreateSimpleOrder(controller, customer.Id, new DateOnly(2026, 8, 27));

        var response = await controller.UpdateOrder(
            created.Id,
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = created.OrderDate,
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        Description = "Wire mesh",
                        Quantity = 30m,
                        Unit = "pcs",
                        Rate = 100m,
                    },
                ],
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var updated = Assert.IsType<OrderDetailDto>(ok.Value);
        Assert.Equal(30m, updated.Items.Single().Quantity);
        Assert.Equal(3000m, updated.GrandTotal);
    }

    [Fact]
    public async Task UpdateOrder_ReactivatesACancelledOrderUsingTheReplacementLineStock()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-REACTIVATE",
            ContactName = "Reactivation Customer",
            Phone = "9876543210",
        };
        var originalProduct = new Product
        {
            ProductCode = "PRD-ORIGINAL",
            Name = "Original mesh",
            Category = "Mesh",
            Unit = "pcs",
            QuantityOnHand = 100m,
        };
        var replacementProduct = new Product
        {
            ProductCode = "PRD-REPLACEMENT",
            Name = "Replacement mesh",
            Category = "Mesh",
            Unit = "pcs",
            QuantityOnHand = 100m,
        };
        var order = new SalesOrder
        {
            OrderNumber = "01/26-27",
            Customer = customer,
            OrderDate = new DateOnly(2026, 8, 27),
            Status = "Pending",
            Items =
            [
                new SalesOrderItem
                {
                    Product = originalProduct,
                    Description = originalProduct.Name,
                    Quantity = 20m,
                    Unit = "pcs",
                    Rate = 10m,
                },
            ],
        };
        OrderCalculator.Recalculate(order);
        database.Products.Add(replacementProduct);
        database.SalesOrders.Add(order);
        await database.SaveChangesAsync();

        var controller = new OrdersController(database);
        var cancelled = await controller.UpdateStatus(
            order.Id,
            new OrderStatusRequest { Status = "Cancelled" },
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(cancelled.Result);
        database.ChangeTracker.Clear();

        var reactivated = await controller.UpdateOrder(
            order.Id,
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = order.OrderDate,
                Status = "Pending",
                Items =
                [
                    new OrderItemRequest
                    {
                        ProductId = replacementProduct.Id,
                        Description = replacementProduct.Name,
                        Quantity = 30m,
                        Unit = "pcs",
                        Rate = 10m,
                    },
                ],
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(reactivated.Result);
        var detail = Assert.IsType<OrderDetailDto>(ok.Value);
        Assert.Equal("Pending", detail.Status);
        Assert.Equal(30m, detail.Items.Single().Quantity);

        database.ChangeTracker.Clear();
        var persistedOriginal = await database.Products.SingleAsync(item => item.Id == originalProduct.Id);
        var persistedReplacement = await database.Products.SingleAsync(item => item.Id == replacementProduct.Id);
        Assert.Equal(100m, persistedOriginal.QuantityOnHand);
        Assert.Equal(0m, persistedOriginal.TotalSold);
        Assert.Equal(70m, persistedReplacement.QuantityOnHand);
        Assert.Equal(30m, persistedReplacement.TotalSold);
    }

    [Fact]
    public async Task UpdateOrder_CannotMoveAnIssuedInvoiceIntoADifferentFinancialYear()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-INVOICE-DATE",
            ContactName = "Invoice Date Customer",
            Phone = "9876543210",
        };
        database.Customers.Add(customer);
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);
        var created = await CreateSimpleOrder(controller, customer.Id, new DateOnly(2026, 4, 1));

        var response = await controller.UpdateOrder(
            created.Id,
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = new DateOnly(2027, 4, 1),
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

        var badRequest = Assert.IsType<BadRequestObjectResult>(response.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains(nameof(OrderRequest.OrderDate), problem.Errors.Keys);

        database.ChangeTracker.Clear();
        var persisted = await database.SalesOrders.SingleAsync(item => item.Id == created.Id);
        Assert.Equal(new DateOnly(2026, 4, 1), persisted.OrderDate);
        Assert.Equal("01/26-27", persisted.OrderNumber);
    }

    [Fact]
    public async Task DeleteOrder_RestoresStockAndKeepsRecordedPaymentsWithTheCustomer()
    {
        await using var database = CreateDatabase();
        var customer = new Customer
        {
            CustomerCode = "CUS-DELETE",
            ContactName = "Delete Customer",
            Phone = "9876543210",
        };
        var product = new Product
        {
            ProductCode = "PRD-DELETE",
            Name = "Delete mesh",
            Category = "Mesh",
            Unit = "pcs",
            QuantityOnHand = 10m,
        };
        database.AddRange(customer, product);
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);
        var createdResponse = await controller.CreateOrder(
            new OrderRequest
            {
                CustomerId = customer.Id,
                OrderDate = new DateOnly(2026, 8, 27),
                Status = "Pending",
                PaidAmount = 25m,
                Items =
                [
                    new OrderItemRequest
                    {
                        ProductId = product.Id,
                        Description = product.Name,
                        Quantity = 4m,
                        Unit = "pcs",
                        Rate = 100m,
                    },
                ],
            },
            CancellationToken.None);
        var createdResult = Assert.IsType<CreatedAtActionResult>(createdResponse.Result);
        var created = Assert.IsType<OrderDetailDto>(createdResult.Value);

        var response = await controller.DeleteOrder(created.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        database.ChangeTracker.Clear();
        Assert.False(await database.SalesOrders.AnyAsync(item => item.Id == created.Id));
        var persistedProduct = await database.Products.SingleAsync(item => item.Id == product.Id);
        Assert.Equal(10m, persistedProduct.QuantityOnHand);
        Assert.Equal(0m, persistedProduct.TotalSold);
        var payment = await database.Payments.SingleAsync();
        Assert.Equal(customer.Id, payment.CustomerId);
        Assert.Null(payment.SalesOrderId);
    }

    [Fact]
    public async Task GetInvoiceData_IncludesTheCurrentCompanyProfile()
    {
        await using var database = CreateDatabase();
        var order = await SeedPaidOrder(database);
        database.CompanyProfiles.Add(new CompanyProfile
        {
            Id = CompanyProfile.DefaultId,
            CompanyName = "Kanchi Mesh",
            Address = "No. 10, Industrial Estate\nChennai",
            BankName = "Example Bank",
            BankAccountNumber = "1234567890",
            BankIfscCode = "EXAM0000123",
        });
        await database.SaveChangesAsync();
        var controller = new OrdersController(database);

        var response = await controller.GetInvoiceData(order.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var detail = Assert.IsType<OrderDetailDto>(ok.Value);
        Assert.NotNull(detail.Company);
        Assert.Equal("Kanchi Mesh", detail.Company!.CompanyName);
        Assert.Equal("No. 10, Industrial Estate\nChennai", detail.Company.Address);
        Assert.Equal("1234567890", detail.Company.BankAccountNumber);
    }

    private static async Task<OrderDetailDto> CreateSimpleOrder(
        OrdersController controller,
        Guid customerId,
        DateOnly orderDate)
    {
        var response = await controller.CreateOrder(
            new OrderRequest
            {
                CustomerId = customerId,
                OrderDate = orderDate,
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

        var created = Assert.IsType<CreatedAtActionResult>(response.Result);
        return Assert.IsType<OrderDetailDto>(created.Value);
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
