using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace KanchimeshAPI.Controllers;

[Route("api/payments")]
public sealed class PaymentsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PaymentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PaymentDto>>> GetPayments(
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? orderId,
        [FromQuery] bool? isAdvance,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.Payments.AsNoTracking()
            .Include(payment => payment.Customer)
            .Include(payment => payment.SalesOrder)
            .AsQueryable();
        if (customerId.HasValue)
        {
            query = query.Where(payment => payment.CustomerId == customerId.Value);
        }

        if (orderId.HasValue)
        {
            query = query.Where(payment => payment.SalesOrderId == orderId.Value);
        }

        if (isAdvance.HasValue)
        {
            query = query.Where(payment => payment.IsAdvance == isAdvance.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var payments = await query
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResult<PaymentDto>(payments.Select(payment => payment.ToDto()).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(PaymentSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaymentSummaryDto>> GetSummary(CancellationToken cancellationToken)
    {
        var sales = await database.SalesOrders.AsNoTracking()
            .Where(order => order.Status != "Cancelled")
            .SumAsync(order => (decimal?)order.GrandTotal, cancellationToken) ?? 0m;
        var payments = await database.Payments.AsNoTracking()
            .Include(payment => payment.SalesOrder)
            .ToListAsync(cancellationToken);
        var validPayments = payments.Where(payment => payment.SalesOrder is null || payment.SalesOrder.Status != "Cancelled").ToList();
        var totalReceived = validPayments.Sum(payment => payment.Amount);
        var totalAdvance = validPayments.Where(payment => payment.IsAdvance).Sum(payment => payment.Amount);
        var appliedToOrders = validPayments.Where(payment => !payment.IsAdvance && payment.SalesOrderId.HasValue).Sum(payment => payment.Amount);
        return Ok(new PaymentSummaryDto(sales, totalReceived, Math.Max(sales - appliedToOrders, 0m), totalAdvance, appliedToOrders));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentDto>> GetPayment(Guid id, CancellationToken cancellationToken)
    {
        var payment = await GetPaymentGraph(id, tracking: false, cancellationToken);
        return payment is null ? NotFound() : Ok(payment.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PaymentDto>> CreatePayment(PaymentRequest request, CancellationToken cancellationToken)
    {
        var compatibilityError = await ResolveCompatibilityFields(request, cancellationToken);
        if (compatibilityError is not null)
        {
            return ValidationError(compatibilityError.Value.Field, compatibilityError.Value.Message);
        }

        if (!WorkflowValues.TryNormalize(request.Method, WorkflowValues.PaymentMethods, out var method))
        {
            return ValidationError(nameof(request.Method), $"Method must be one of: {string.Join(", ", WorkflowValues.PaymentMethods)}.");
        }

        await using var transaction = await BeginPaymentTransaction(cancellationToken);
        var relationError = await ValidateRelations(request, null, cancellationToken);
        if (relationError is not null)
        {
            return ValidationError(relationError.Value.Field, relationError.Value.Message);
        }

        var payment = new Payment { PaymentNumber = DocumentNumbers.New("PAY") };
        Apply(payment, request, method);
        database.Payments.Add(payment);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var savedPayment = await GetPaymentGraph(payment.Id, tracking: false, cancellationToken);
        return CreatedAtAction(nameof(GetPayment), new { payment.Id }, savedPayment!.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentDto>> UpdatePayment(
        Guid id,
        PaymentRequest request,
        CancellationToken cancellationToken)
    {
        var compatibilityError = await ResolveCompatibilityFields(request, cancellationToken);
        if (compatibilityError is not null)
        {
            return ValidationError(compatibilityError.Value.Field, compatibilityError.Value.Message);
        }

        if (!WorkflowValues.TryNormalize(request.Method, WorkflowValues.PaymentMethods, out var method))
        {
            return ValidationError(nameof(request.Method), $"Method must be one of: {string.Join(", ", WorkflowValues.PaymentMethods)}.");
        }

        await using var transaction = await BeginPaymentTransaction(cancellationToken);
        var payment = await GetPaymentGraph(id, tracking: true, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        var relationError = await ValidateRelations(request, id, cancellationToken);
        if (relationError is not null)
        {
            return ValidationError(relationError.Value.Field, relationError.Value.Message);
        }

        Apply(payment, request, method);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var savedPayment = await GetPaymentGraph(payment.Id, tracking: false, cancellationToken);
        return Ok(savedPayment!.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePayment(Guid id, CancellationToken cancellationToken)
    {
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        database.Payments.Remove(payment);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(string Field, string Message)?> ValidateRelations(
        PaymentRequest request,
        Guid? excludedPaymentId,
        CancellationToken cancellationToken)
    {
        if (!await database.Customers.AnyAsync(customer => customer.Id == request.CustomerId && customer.IsActive, cancellationToken))
        {
            return (nameof(request.CustomerId), "The selected active customer does not exist.");
        }

        if (request.IsAdvance && request.SalesOrderId.HasValue)
        {
            return (nameof(request.SalesOrderId), "An advance payment cannot be linked to a specific order.");
        }

        if (!request.IsAdvance && !request.SalesOrderId.HasValue)
        {
            return (nameof(request.SalesOrderId), "A non-advance payment must be linked to a sales order.");
        }

        if (request.IsAdvance)
        {
            return null;
        }

        if (!request.SalesOrderId.HasValue)
        {
            return null;
        }

        IQueryable<SalesOrder> orderQuery = database.SalesOrders.Include(item => item.Payments);
        if (database.Database.IsSqlServer())
        {
            orderQuery = database.SalesOrders
                .FromSqlInterpolated($"SELECT * FROM [SalesOrders] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {request.SalesOrderId.Value}")
                .Include(item => item.Payments);
        }

        var order = await orderQuery.SingleOrDefaultAsync(item => item.Id == request.SalesOrderId.Value, cancellationToken);
        if (order is null)
        {
            return (nameof(request.SalesOrderId), "The selected order does not exist.");
        }

        if (order.CustomerId != request.CustomerId)
        {
            return (nameof(request.SalesOrderId), "The selected order belongs to a different customer.");
        }

        if (string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return (nameof(request.SalesOrderId), "Payments cannot be added to a cancelled order.");
        }

        var alreadyPaid = order.Payments
            .Where(payment => payment.Id != excludedPaymentId && !payment.IsAdvance)
            .Sum(payment => payment.Amount);
        if (alreadyPaid + request.Amount > order.GrandTotal)
        {
            return (nameof(request.Amount), "This payment exceeds the outstanding amount for the selected order.");
        }

        return null;
    }

    private async Task<(string Field, string Message)?> ResolveCompatibilityFields(PaymentRequest request, CancellationToken cancellationToken)
    {
        if (request.Date.HasValue)
        {
            request.PaymentDate = DateOnly.FromDateTime(request.Date.Value);
        }

        if (!request.SalesOrderId.HasValue && !string.IsNullOrWhiteSpace(request.OrderId))
        {
            if (Guid.TryParse(request.OrderId, out var parsedOrderId))
            {
                request.SalesOrderId = parsedOrderId;
            }
            else
            {
                var orderNumber = request.OrderId.Trim();
                request.SalesOrderId = await database.SalesOrders.AsNoTracking()
                    .Where(order => order.OrderNumber == orderNumber)
                    .Select(order => (Guid?)order.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!request.SalesOrderId.HasValue)
                {
                    return (nameof(request.OrderId), "No sales order matches the supplied order ID.");
                }
            }
        }

        if (request.CustomerId != Guid.Empty)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
        {
            var customerName = request.CustomerName.Trim().ToLower();
            request.CustomerId = await database.Customers.AsNoTracking()
                .Where(customer => customer.IsActive &&
                    (customer.ContactName.ToLower() == customerName ||
                     (customer.CompanyName ?? string.Empty).ToLower() == customerName))
                .OrderBy(customer => customer.CreatedAtUtc)
                .Select(customer => customer.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (request.CustomerId != Guid.Empty)
            {
                return null;
            }

            return (nameof(request.CustomerName), "No active customer matches the supplied customer name.");
        }

        if (request.SalesOrderId.HasValue)
        {
            request.CustomerId = await database.SalesOrders.AsNoTracking()
                .Where(order => order.Id == request.SalesOrderId.Value)
                .Select(order => order.CustomerId)
                .FirstOrDefaultAsync(cancellationToken);
            if (request.CustomerId != Guid.Empty)
            {
                return null;
            }
        }

        return (nameof(request.CustomerId), "customerId is required. The Flutter wireframe may supply customerName with orderId instead.");
    }

    private static void Apply(Payment payment, PaymentRequest request, string method)
    {
        payment.CustomerId = request.CustomerId;
        payment.SalesOrderId = request.SalesOrderId;
        payment.Amount = request.Amount;
        payment.PaymentDate = request.PaymentDate;
        payment.Method = method;
        payment.Reference = Null(request.Reference);
        payment.Notes = Null(request.Notes);
        payment.IsAdvance = request.IsAdvance;
    }

    private async Task<Payment?> GetPaymentGraph(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        IQueryable<Payment> query = database.Payments
            .Include(payment => payment.Customer)
            .Include(payment => payment.SalesOrder);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(payment => payment.Id == id, cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginPaymentTransaction(CancellationToken cancellationToken)
    {
        if (!database.Database.IsSqlServer())
        {
            return null;
        }

        return await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
