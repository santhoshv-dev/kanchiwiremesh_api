using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/enquiries")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
public sealed class EnquiriesController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<EnquiryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EnquiryDto>>> GetEnquiries(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.Enquiries.AsNoTracking().Include(enquiry => enquiry.Customer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusTerm = status.Trim().ToLower();
            query = query.Where(enquiry => enquiry.Status.ToLower() == statusTerm);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(enquiry =>
                enquiry.ContactName.ToLower().Contains(term) ||
                (enquiry.CompanyName ?? string.Empty).ToLower().Contains(term) ||
                enquiry.Phone.Contains(term) ||
                (enquiry.ProductRequirement ?? string.Empty).ToLower().Contains(term) ||
                enquiry.EnquiryNumber.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var enquiries = await query
            .OrderByDescending(enquiry => enquiry.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResult<EnquiryDto>(enquiries.Select(enquiry => enquiry.ToDto()).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EnquiryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnquiryDto>> GetEnquiry(Guid id, CancellationToken cancellationToken)
    {
        var enquiry = await database.Enquiries.AsNoTracking()
            .Include(item => item.Customer)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return enquiry is null ? NotFound() : Ok(enquiry.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(EnquiryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<EnquiryDto>> CreateEnquiry(EnquiryRequest request, CancellationToken cancellationToken)
    {
        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.EnquiryStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.EnquiryStatuses)}.");
        }

        if (request.CustomerId.HasValue && !await CustomerExists(request.CustomerId.Value, cancellationToken))
        {
            return ValidationError(nameof(request.CustomerId), "The selected customer does not exist.");
        }

        var enquiry = new Enquiry { EnquiryNumber = DocumentNumbers.New("ENQ") };
        Apply(enquiry, request, status);
        database.Enquiries.Add(enquiry);
        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(enquiry).Reference(item => item.Customer).LoadAsync(cancellationToken);
        return CreatedAtAction(nameof(GetEnquiry), new { enquiry.Id }, enquiry.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(EnquiryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnquiryDto>> UpdateEnquiry(Guid id, EnquiryRequest request, CancellationToken cancellationToken)
    {
        if (!WorkflowValues.TryNormalize(request.Status, WorkflowValues.EnquiryStatuses, out var status))
        {
            return ValidationError(nameof(request.Status), $"Status must be one of: {string.Join(", ", WorkflowValues.EnquiryStatuses)}.");
        }

        if (request.CustomerId.HasValue && !await CustomerExists(request.CustomerId.Value, cancellationToken))
        {
            return ValidationError(nameof(request.CustomerId), "The selected customer does not exist.");
        }

        var enquiry = await database.Enquiries
            .Include(item => item.Customer)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (enquiry is null)
        {
            return NotFound();
        }

        Apply(enquiry, request, status);
        await database.SaveChangesAsync(cancellationToken);
        await database.Entry(enquiry).Reference(item => item.Customer).LoadAsync(cancellationToken);
        return Ok(enquiry.ToDto());
    }

    [HttpPost("{id:guid}/convert-to-customer")]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetailDto>> ConvertToCustomer(Guid id, CancellationToken cancellationToken)
    {
        var enquiry = await database.Enquiries
            .Include(item => item.Customer)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (enquiry is null)
        {
            return NotFound();
        }

        Customer customer;
        if (enquiry.Customer is not null)
        {
            customer = enquiry.Customer;
        }
        else
        {
            customer = new Customer
            {
                CustomerCode = DocumentNumbers.New("CUS"),
                ContactName = enquiry.ContactName,
                CompanyName = enquiry.CompanyName,
                Phone = enquiry.Phone,
                Email = enquiry.Email,
                Notes = enquiry.Note
            };
            database.Customers.Add(customer);
            enquiry.CustomerId = customer.Id;
            enquiry.Customer = customer;
        }

        enquiry.Status = "Converted";
        await database.SaveChangesAsync(cancellationToken);
        return Ok(new CustomerDetailDto(
            customer.Id, customer.CustomerCode, customer.ContactName, customer.CompanyName,
            customer.Phone, customer.AlternatePhone, customer.WhatsAppNumber, customer.Email,
            customer.Address, customer.City, customer.District, customer.State, customer.PostalCode,
            customer.GstNumber, customer.BusinessType, customer.Notes, customer.IsActive,
            0, 0m, 0m, 0m, customer.CreatedAtUtc, customer.UpdatedAtUtc));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteEnquiry(Guid id, CancellationToken cancellationToken)
    {
        var enquiry = await database.Enquiries.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (enquiry is null)
        {
            return NotFound();
        }

        database.Enquiries.Remove(enquiry);
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<bool> CustomerExists(Guid customerId, CancellationToken cancellationToken) =>
        await database.Customers.AnyAsync(customer => customer.Id == customerId && customer.IsActive, cancellationToken);

    private static void Apply(Enquiry enquiry, EnquiryRequest request, string status)
    {
        enquiry.CustomerId = request.CustomerId;
        enquiry.ContactName = request.ContactName.Trim();
        enquiry.CompanyName = Null(request.CompanyName);
        enquiry.Phone = request.Phone.Trim();
        enquiry.Email = Null(request.Email);
        enquiry.ProductRequirement = Null(request.ProductRequirement);
        enquiry.Quantity = request.Quantity;
        enquiry.Unit = Null(request.Unit);
        enquiry.Message = Null(request.Message);
        enquiry.Note = Null(request.Note);
        enquiry.Status = status;
        enquiry.FollowUpDate = request.FollowUpDate;
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
