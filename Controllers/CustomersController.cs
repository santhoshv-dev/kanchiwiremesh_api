using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/customers")]
public sealed class CustomersController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CustomerListItemDto>>> GetCustomers(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.Customers.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(customer => customer.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(customer =>
                customer.ContactName.ToLower().Contains(term) ||
                (customer.CompanyName ?? string.Empty).ToLower().Contains(term) ||
                customer.Phone.Contains(term) ||
                customer.CustomerCode.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var customers = await query
            .Include(customer => customer.Orders)
            .Include(customer => customer.Payments)
                .ThenInclude(payment => payment.SalesOrder)
            .AsSplitQuery()
            .OrderBy(customer => customer.CompanyName ?? customer.ContactName)
            .ThenBy(customer => customer.ContactName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<CustomerListItemDto>(
            customers.Select(ToListDto).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> GetCustomer(Guid id, CancellationToken cancellationToken)
    {
        var customer = await database.Customers.AsNoTracking()
            .Include(item => item.Orders)
            .Include(item => item.Payments)
                .ThenInclude(payment => payment.SalesOrder)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return customer is null ? NotFound() : Ok(ToDetailDto(customer));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<CustomerDetailDto>> CreateCustomer(
        CustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = new Customer { CustomerCode = DocumentNumbers.New("CUS") };
        Apply(customer, request);
        database.Customers.Add(customer);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetCustomer), new { customer.Id }, ToDetailDto(customer));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> UpdateCustomer(
        Guid id,
        CustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await database.Customers
            .Include(item => item.Orders)
            .Include(item => item.Payments)
                .ThenInclude(payment => payment.SalesOrder)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        Apply(customer, request);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToDetailDto(customer));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCustomer(Guid id, CancellationToken cancellationToken)
    {
        var customer = await database.Customers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        // Customers retain financial history, so deletion safely deactivates the account.
        customer.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/ledger")]
    [ProducesResponseType(typeof(CustomerLedgerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerLedgerDto>> GetLedger(Guid id, CancellationToken cancellationToken)
    {
        var customer = await database.Customers.AsNoTracking()
            .Include(item => item.Orders)
            .Include(item => item.Payments)
                .ThenInclude(payment => payment.SalesOrder)
            .AsSplitQuery()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        var entries = new List<(DateOnly Date, int SortOrder, string Type, string Description, decimal Debit, decimal Credit, Guid Id)>();
        entries.AddRange(customer.Orders
            .Where(order => !string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .Select(order => (order.OrderDate, 0, "Order", $"Order #{order.OrderNumber}", order.GrandTotal, 0m, order.Id)));
        entries.AddRange(customer.Payments
            .Where(payment => payment.SalesOrder is null || !string.Equals(payment.SalesOrder.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .Select(payment => (payment.PaymentDate, 1, payment.IsAdvance ? "Advance" : "Payment",
                $"{payment.Method} payment #{payment.PaymentNumber}", 0m, payment.Amount, payment.Id)));

        decimal balance = 0m;
        var transactions = entries
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.Id)
            .Select(entry =>
            {
                balance += entry.Debit - entry.Credit;
                return new LedgerTransactionDto(entry.Date, entry.Type, entry.Description,
                    entry.Debit, entry.Credit, balance, entry.Id);
            })
            .ToList();

        var totalSales = customer.Orders
            .Where(order => !string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .Sum(order => order.GrandTotal);
        var totalPaid = ValidPayments(customer).Sum(payment => payment.Amount);
        return Ok(new CustomerLedgerDto(
            customer.Id,
            DtoMappings.DisplayCustomerName(customer),
            totalSales,
            totalPaid,
            Math.Max(totalSales - totalPaid, 0m),
            transactions));
    }

    private static CustomerListItemDto ToListDto(Customer customer)
    {
        var orders = customer.Orders.Where(order => !string.Equals(order.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)).ToList();
        var totalSales = orders.Sum(order => order.GrandTotal);
        var totalPaid = ValidPayments(customer).Sum(payment => payment.Amount);
        return new CustomerListItemDto(
            customer.Id,
            customer.CustomerCode,
            customer.ContactName,
            customer.CompanyName,
            customer.Phone,
            customer.City,
            customer.IsActive,
            orders.Count,
            totalSales,
            totalPaid,
            Math.Max(totalSales - totalPaid, 0m));
    }

    private static CustomerDetailDto ToDetailDto(Customer customer)
    {
        var list = ToListDto(customer);
        return new CustomerDetailDto(
            customer.Id,
            customer.CustomerCode,
            customer.ContactName,
            customer.CompanyName,
            customer.Phone,
            customer.AlternatePhone,
            customer.WhatsAppNumber,
            customer.Email,
            customer.Address,
            customer.City,
            customer.District,
            customer.State,
            customer.PostalCode,
            customer.GstNumber,
            customer.BusinessType,
            customer.Notes,
            customer.IsActive,
            list.OrderCount,
            list.TotalSales,
            list.TotalPaid,
            list.Outstanding,
            customer.CreatedAtUtc,
            customer.UpdatedAtUtc);
    }

    private static void Apply(Customer customer, CustomerRequest request)
    {
        customer.ContactName = request.ContactName.Trim();
        customer.CompanyName = Null(request.CompanyName) ?? Null(request.Company);
        customer.Phone = request.Phone.Trim();
        customer.AlternatePhone = Null(request.AlternatePhone);
        customer.WhatsAppNumber = Null(request.WhatsAppNumber);
        customer.Email = Null(request.Email);
        customer.Address = Null(request.Address);
        customer.City = Null(request.City);
        customer.District = Null(request.District);
        customer.State = Null(request.State);
        customer.PostalCode = Null(request.PostalCode);
        customer.GstNumber = Null(request.GstNumber)?.ToUpperInvariant();
        customer.BusinessType = Null(request.BusinessType);
        customer.Notes = Null(request.Notes);
        customer.IsActive = request.IsActive;
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IEnumerable<Payment> ValidPayments(Customer customer) => customer.Payments
        .Where(payment => payment.SalesOrder is null || !string.Equals(payment.SalesOrder.Status, "Cancelled", StringComparison.OrdinalIgnoreCase));
}
