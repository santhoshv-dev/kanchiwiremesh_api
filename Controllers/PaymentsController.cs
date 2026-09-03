using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var orderSales = await database.SalesOrders.AsNoTracking()
            .Where(order => order.Status != "Cancelled")
            .SumAsync(order => (decimal?)order.GrandTotal, cancellationToken) ?? 0m;
        // Opening balances are debit-side customer balances, just like sales
        // orders. Include them here so a receipt recorded directly against a
        // customer changes the same outstanding balance in every financial
        // summary.
        var openingBalances = await database.Customers.AsNoTracking()
            .SumAsync(customer => (decimal?)customer.OpeningBalance, cancellationToken) ?? 0m;
        var sales = orderSales + openingBalances;
        var payments = await database.Payments.AsNoTracking()
            .Include(payment => payment.SalesOrder)
            .ToListAsync(cancellationToken);
        var validPayments = payments.Where(payment => payment.SalesOrder is null || payment.SalesOrder.Status != "Cancelled").ToList();
        var totalReceived = validPayments.Sum(payment => payment.Amount);
        var totalAdvance = validPayments.Where(payment => payment.IsAdvance).Sum(payment => payment.Amount);
        var appliedToOrders = validPayments.Where(payment => !payment.IsAdvance && payment.SalesOrderId.HasValue).Sum(payment => payment.Amount);
        return Ok(new PaymentSummaryDto(sales, totalReceived, Math.Max(sales - totalReceived, 0m), totalAdvance, appliedToOrders));
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

        var paymentId = Guid.NewGuid();
        try
        {
            if (database.Database.IsRelational())
            {
                // SQL Server retries are enabled on the context. An explicit
                // transaction must be executed through its retry strategy.
                await ExecutionStrategyExtensions.ExecuteInTransactionAsync<Guid, Guid>(
                    database.Database.CreateExecutionStrategy(),
                    paymentId,
                    async (id, retryCancellationToken) =>
                    {
                        database.ChangeTracker.Clear();
                        await SaveNewPayment(id, request, method, retryCancellationToken);
                        return id;
                    },
                    (id, retryCancellationToken) => database.Payments
                        .AsNoTracking()
                        .AnyAsync(payment => payment.Id == id, retryCancellationToken),
                    (context, retryCancellationToken) => context.Database
                        .BeginTransactionAsync(IsolationLevel.Serializable, retryCancellationToken),
                    cancellationToken);
            }
            else
            {
                await SaveNewPayment(paymentId, request, method, cancellationToken);
            }
        }
        catch (PaymentValidationException exception)
        {
            return ValidationError(exception.Field, exception.Message);
        }

        var savedPayment = await GetPaymentGraph(paymentId, tracking: false, cancellationToken);
        if (savedPayment is null)
        {
            throw new InvalidOperationException("The newly created payment could not be reloaded.");
        }

        return CreatedAtAction(nameof(GetPayment), new { savedPayment.Id }, savedPayment.ToDto());
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

        var updateStartedAtUtc = DateTime.UtcNow;
        try
        {
            if (database.Database.IsRelational())
            {
                await ExecutionStrategyExtensions.ExecuteInTransactionAsync<Guid, Guid>(
                    database.Database.CreateExecutionStrategy(),
                    id,
                    async (paymentId, retryCancellationToken) =>
                    {
                        database.ChangeTracker.Clear();
                        await SaveUpdatedPayment(paymentId, request, method, retryCancellationToken);
                        return paymentId;
                    },
                    (paymentId, retryCancellationToken) => PaymentMatchesRequest(
                        paymentId, request, method, updateStartedAtUtc, retryCancellationToken),
                    (context, retryCancellationToken) => context.Database
                        .BeginTransactionAsync(IsolationLevel.Serializable, retryCancellationToken),
                    cancellationToken);
            }
            else
            {
                await SaveUpdatedPayment(id, request, method, cancellationToken);
            }
        }
        catch (PaymentNotFoundException)
        {
            return NotFound();
        }
        catch (PaymentValidationException exception)
        {
            return ValidationError(exception.Field, exception.Message);
        }

        var savedPayment = await GetPaymentGraph(id, tracking: false, cancellationToken);
        if (savedPayment is null)
        {
            throw new InvalidOperationException("The updated payment could not be reloaded.");
        }

        return Ok(savedPayment.ToDto());
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

        var orderId = payment.SalesOrderId;
        database.Payments.Remove(payment);
        await SyncLinkedOrderCompletionAsync(cancellationToken, orderId);

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

    private async Task SaveNewPayment(
        Guid paymentId,
        PaymentRequest request,
        string method,
        CancellationToken cancellationToken)
    {
        var relationError = await ValidateRelations(request, null, cancellationToken);
        if (relationError is not null)
        {
            throw new PaymentValidationException(relationError.Value.Field, relationError.Value.Message);
        }

        var payment = new Payment
        {
            Id = paymentId,
            PaymentNumber = DocumentNumbers.New("PAY"),
        };
        Apply(payment, request, method);
        database.Payments.Add(payment);
        await SyncLinkedOrderCompletionAsync(cancellationToken, payment.SalesOrderId);
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveUpdatedPayment(
        Guid paymentId,
        PaymentRequest request,
        string method,
        CancellationToken cancellationToken)
    {
        var payment = await GetPaymentGraph(paymentId, tracking: true, cancellationToken);
        if (payment is null)
        {
            throw new PaymentNotFoundException();
        }

        var previousOrderId = payment.SalesOrderId;
        var relationError = await ValidateRelations(request, paymentId, cancellationToken);
        if (relationError is not null)
        {
            throw new PaymentValidationException(relationError.Value.Field, relationError.Value.Message);
        }

        Apply(payment, request, method);
        // A payment can be moved from an order to the customer's unlinked
        // balance (or to another order). Re-evaluate both sides so an old
        // order never remains marked complete after its receipt is removed.
        await SyncLinkedOrderCompletionAsync(cancellationToken, previousOrderId, payment.SalesOrderId);

        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncLinkedOrderCompletionAsync(CancellationToken cancellationToken, params Guid?[] orderIds)
    {
        foreach (var orderId in orderIds
                     .Where(id => id.HasValue)
                     .Select(id => id!.Value)
                     .Distinct())
        {
            var order = await database.SalesOrders.FindAsync([orderId], cancellationToken);
            if (order is not null)
            {
                await OrderCalculator.SyncOrderCompletionAsync(database, order, cancellationToken);
            }
        }
    }

    private Task<bool> PaymentMatchesRequest(
        Guid paymentId,
        PaymentRequest request,
        string method,
        DateTime updateStartedAtUtc,
        CancellationToken cancellationToken) =>
        database.Payments.AsNoTracking().AnyAsync(payment =>
            payment.Id == paymentId &&
            payment.CustomerId == request.CustomerId &&
            payment.SalesOrderId == request.SalesOrderId &&
            payment.Amount == request.Amount &&
            payment.PaymentDate == request.PaymentDate &&
            payment.Method == method &&
            payment.Reference == Null(request.Reference) &&
            payment.Notes == Null(request.Notes) &&
            payment.IsAdvance == request.IsAdvance &&
            payment.UpdatedAtUtc >= updateStartedAtUtc,
            cancellationToken);

    private sealed class PaymentValidationException(string field, string message) : Exception(message)
    {
        public string Field { get; } = field;
    }

    private sealed class PaymentNotFoundException : Exception
    {
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
