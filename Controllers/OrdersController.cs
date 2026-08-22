using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/orders")]
public sealed class OrdersController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] Guid? customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.SalesOrders.AsNoTracking()
            .Include(order => order.Customer)
            .Include(order => order.Payments)
            .Include(order => order.Items)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusTerm = status.Trim().ToLower();
            query = query.Where(order => order.Status.ToLower() == statusTerm);
        }

        if (customerId.HasValue)
        {
            query = query.Where(order => order.CustomerId == customerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(order =>
                order.OrderNumber.ToLower().Contains(term) ||
                order.Customer.ContactName.ToLower().Contains(term) ||
                (order.Customer.CompanyName ?? string.Empty).ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orders = await query
            .OrderByDescending(order => order.OrderDate)
            .ThenByDescending(order => order.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResult<OrderSummaryDto>(orders.Select(ToSummaryDto).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await GetOrderGraph(id, tracking: false, cancellationToken);
        return order is null ? NotFound() : Ok(ToDetailDto(order));
    }

    // Invoice screens can use the same immutable, tax-inclusive order payload without a separate PDF service.
    [HttpGet("{id:guid}/invoice")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> GetInvoiceData(Guid id, CancellationToken cancellationToken) =>
        await GetOrder(id, cancellationToken);

    [HttpPost]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderDetailDto>> CreateOrder(OrderRequest request, CancellationToken cancellationToken)
    {
        var compatibilityError = await ResolveCompatibilityFields(request, cancellationToken);
        if (compatibilityError is not null)
        {
            return ValidationError(compatibilityError.Value.Field, compatibilityError.Value.Message);
        }

        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.OrderStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.OrderStatuses)}.");
        }

        var relationError = await ValidateRelations(request, cancellationToken);
        if (relationError is not null)
        {
            return ValidationError(relationError.Value.Field, relationError.Value.Message);
        }

        var order = new SalesOrder { OrderNumber = DocumentNumbers.New("ORD") };
        ReplaceOrderValues(order, request, status);
        database.SalesOrders.Add(order);
        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(order).Reference(item => item.Customer).LoadAsync(cancellationToken);
        return CreatedAtAction(nameof(GetOrder), new { order.Id }, ToDetailDto(order));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> UpdateOrder(
        Guid id,
        OrderRequest request,
        CancellationToken cancellationToken)
    {
        var compatibilityError = await ResolveCompatibilityFields(request, cancellationToken);
        if (compatibilityError is not null)
        {
            return ValidationError(compatibilityError.Value.Field, compatibilityError.Value.Message);
        }

        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.OrderStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.OrderStatuses)}.");
        }

        var relationError = await ValidateRelations(request, cancellationToken);
        if (relationError is not null)
        {
            return ValidationError(relationError.Value.Field, relationError.Value.Message);
        }

        var order = await GetOrderGraph(id, tracking: true, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Payments.Count > 0 && order.CustomerId != request.CustomerId)
        {
            return ValidationError(nameof(request.CustomerId), "The customer cannot be changed after payments have been recorded for an order.");
        }

        ReplaceOrderValues(order, request, status);
        var appliedPaymentTotal = order.Payments.Where(payment => !payment.IsAdvance).Sum(payment => payment.Amount);
        if (!string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) && order.GrandTotal < appliedPaymentTotal)
        {
            return ValidationError(nameof(request.Items), "The updated total cannot be less than payments already applied to this order.");
        }

        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(order).Reference(item => item.Customer).LoadAsync(cancellationToken);
        return Ok(ToDetailDto(order));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> UpdateStatus(
        Guid id,
        OrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.OrderStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.OrderStatuses)}.");
        }

        var order = await GetOrderGraph(id, tracking: true, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) && order.Payments.Count > 0)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "This order has recorded payments.",
                Detail = "Reverse or reassign the payments before cancelling the order."
            });
        }

        order.Status = status;
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToDetailDto(order));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteOrder(Guid id, CancellationToken cancellationToken)
    {
        var order = await database.SalesOrders
            .Include(item => item.Payments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Payments.Count > 0)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "This order has recorded payments.",
                Detail = "Cancel the order or delete/reassign its payments before deleting it."
            });
        }

        database.SalesOrders.Remove(order);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(string Field, string Message)?> ValidateRelations(OrderRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedDeliveryDate.HasValue && request.ExpectedDeliveryDate.Value < request.OrderDate)
        {
            return (nameof(request.ExpectedDeliveryDate), "Expected delivery date cannot be before the order date.");
        }

        if (!await database.Customers.AnyAsync(customer => customer.Id == request.CustomerId && customer.IsActive, cancellationToken))
        {
            return (nameof(request.CustomerId), "The selected active customer does not exist.");
        }

        if (request.Items.Count == 0)
        {
            return (nameof(request.Items), "At least one order item is required.");
        }

        var productIds = request.Items
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToList();
        if (productIds.Count > 0)
        {
            var matchedCount = await database.Products.CountAsync(
                product => productIds.Contains(product.Id) && product.IsActive,
                cancellationToken);
            if (matchedCount != productIds.Count)
            {
                return (nameof(request.Items), "Every selected product must exist and be active.");
            }
        }

        return null;
    }

    private async Task<(string Field, string Message)?> ResolveCompatibilityFields(OrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Date.HasValue)
        {
            request.OrderDate = DateOnly.FromDateTime(request.Date.Value);
        }

        if (request.CustomerId == Guid.Empty)
        {
            if (string.IsNullOrWhiteSpace(request.CustomerName))
            {
                return (nameof(request.CustomerId), "customerId is required. The Flutter wireframe may supply customerName instead.");
            }

            var customerName = request.CustomerName.Trim().ToLower();
            var customerId = await database.Customers.AsNoTracking()
                .Where(customer => customer.IsActive &&
                    (customer.ContactName.ToLower() == customerName ||
                     (customer.CompanyName ?? string.Empty).ToLower() == customerName))
                .OrderBy(customer => customer.CreatedAtUtc)
                .Select(customer => customer.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (customerId == Guid.Empty)
            {
                return (nameof(request.CustomerName), "No active customer matches the supplied customer name.");
            }

            request.CustomerId = customerId;
        }

        if (request.Items.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(request.ProductName) || !request.Amount.HasValue)
            {
                return (nameof(request.Items), "items are required. The Flutter wireframe may supply productName and amount instead.");
            }

            var productName = request.ProductName.Trim();
            var productId = await database.Products.AsNoTracking()
                .Where(product => product.IsActive && product.Name.ToLower() == productName.ToLower())
                .Select(product => (Guid?)product.Id)
                .FirstOrDefaultAsync(cancellationToken);
            request.Items =
            [
                new OrderItemRequest
                {
                    ProductId = productId,
                    Description = productName,
                    Quantity = 1m,
                    Unit = "pcs",
                    Rate = request.Amount.Value,
                    GstRate = 0m
                }
            ];
        }

        return null;
    }

    private void ReplaceOrderValues(SalesOrder order, OrderRequest request, string status)
    {
        database.SalesOrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        foreach (var item in request.Items)
        {
            order.Items.Add(new SalesOrderItem
            {
                ProductId = item.ProductId,
                Description = item.Description.Trim(),
                Specification = Null(item.Specification),
                Quantity = item.Quantity,
                Unit = item.Unit.Trim(),
                Rate = item.Rate,
                GstRate = item.GstRate
            });
        }

        order.CustomerId = request.CustomerId;
        order.OrderDate = request.OrderDate;
        order.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        order.Status = status;
        order.Notes = Null(request.Notes);
        order.DiscountAmount = request.DiscountAmount;
        order.FreightAmount = request.FreightAmount;
        OrderCalculator.Recalculate(order);
    }

    private async Task<SalesOrder?> GetOrderGraph(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<SalesOrder> query = database.SalesOrders
            .Include(order => order.Customer)
            .Include(order => order.Items)
            .Include(order => order.Payments)
            .AsSplitQuery();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    private static OrderSummaryDto ToSummaryDto(SalesOrder order)
    {
        var paid = order.Payments.Where(payment => !payment.IsAdvance).Sum(payment => payment.Amount);
        return new OrderSummaryDto(
            order.Id, order.OrderNumber, order.CustomerId, DtoMappings.DisplayCustomerName(order.Customer),
            order.Items.OrderBy(item => item.Id).Select(item => item.Description).FirstOrDefault() ?? "—",
            order.OrderDate, order.ExpectedDeliveryDate, order.Status, order.GrandTotal,
            paid, Math.Max(order.GrandTotal - paid, 0m), order.UpdatedAtUtc);
    }

    private static OrderDetailDto ToDetailDto(SalesOrder order)
    {
        var summary = ToSummaryDto(order);
        return new OrderDetailDto(
            order.Id, order.OrderNumber, order.CustomerId, summary.CustomerName, summary.ProductName, order.OrderDate,
            order.ExpectedDeliveryDate, order.Status, order.Notes, order.Subtotal, order.DiscountAmount,
            order.FreightAmount, order.TaxAmount, order.GrandTotal, summary.PaidAmount, summary.Outstanding,
            order.Items.OrderBy(item => item.Id).Select(item => item.ToDto()).ToList(),
            order.CreatedAtUtc, order.UpdatedAtUtc);
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
