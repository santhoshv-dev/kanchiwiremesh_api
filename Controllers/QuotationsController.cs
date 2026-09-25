using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/quotations")]
public sealed class QuotationsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<QuotationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<QuotationSummaryDto>>> GetQuotations(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] Guid? customerId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);

        var query = database.Quotations
            .AsNoTracking()
            .Include(q => q.Customer)
            .Include(q => q.Items)
                .ThenInclude(i => i.Product)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            var statusTerm = status.Trim().ToLower();
            query = query.Where(q => q.Status.ToLower() == statusTerm);
        }

        if (customerId.HasValue)
        {
            query = query.Where(q => q.CustomerId == customerId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(q => q.QuotationDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(q => q.QuotationDate <= toDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(q =>
                q.QuotationNumber.ToLower().Contains(term) ||
                q.CustomerName.ToLower().Contains(term) ||
                (q.CustomerPhone != null && q.CustomerPhone.ToLower().Contains(term)) ||
                (q.Notes != null && q.Notes.ToLower().Contains(term)) ||
                q.Items.Any(i => i.Description.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(q => q.QuotationDate)
            .ThenByDescending(q => q.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<QuotationSummaryDto>(
            items.Select(ToSummaryDto).ToList(),
            page,
            pageSize,
            totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(QuotationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationDetailDto>> GetQuotation(Guid id, CancellationToken cancellationToken)
    {
        var quotation = await GetQuotationGraph(id, tracking: false, cancellationToken);
        if (quotation is null)
        {
            return NotFound();
        }

        var company = await database.CompanyProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == CompanyProfile.DefaultId, cancellationToken);

        return Ok(ToDetailDto(quotation, CompanySettingsController.ToDto(company)));
    }

    [HttpPost]
    [ProducesResponseType(typeof(QuotationDetailDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<QuotationDetailDto>> CreateQuotation(
        QuotationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            return ValidationError(nameof(request.Items), "At least one item is required in the quotation.");
        }

        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await database.Customers.FindAsync([request.CustomerId.Value], cancellationToken);
            if (customer is null)
            {
                return ValidationError(nameof(request.CustomerId), "Selected customer does not exist.");
            }
        }

        var customerName = (customer?.CompanyName?.Trim().Length > 0 ? customer.CompanyName : customer?.ContactName)
            ?? request.CustomerName?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(customerName))
        {
            return ValidationError(nameof(request.CustomerName), "Customer name is required.");
        }

        var customerPhone = customer?.Phone ?? request.CustomerPhone?.Trim();

        var existingNumbers = await database.Quotations
            .AsNoTracking()
            .Select(q => q.QuotationNumber)
            .ToListAsync(cancellationToken);

        var quotationNumber = GetNextQuotationNumber(request.QuotationDate, existingNumbers);

        var quotation = new Quotation
        {
            Id = Guid.NewGuid(),
            QuotationNumber = quotationNumber,
            CustomerId = customer?.Id,
            CustomerName = customerName,
            CustomerPhone = NullIfWhiteSpace(customerPhone),
            CustomerEmail = NullIfWhiteSpace(customer?.Email ?? request.CustomerEmail),
            CustomerAddress = NullIfWhiteSpace(customer?.Address ?? request.CustomerAddress),
            CustomerGstNumber = NullIfWhiteSpace(customer?.GstNumber ?? request.CustomerGstNumber),
            QuotationDate = request.QuotationDate,
            ValidUntilDate = request.ValidUntilDate ?? request.QuotationDate.AddDays(15),
            Status = WorkflowValues.TryNormalize(request.Status, WorkflowValues.QuotationStatuses, out var normStatus)
                ? normStatus
                : "Draft",
            GstType = NullIfWhiteSpace(request.GstType) ?? "IGST",
            Notes = NullIfWhiteSpace(request.Notes),
            TermsAndConditions = NullIfWhiteSpace(request.TermsAndConditions) ?? DefaultTerms,
            DiscountAmount = Math.Max(0m, request.DiscountAmount),
            FreightAmount = Math.Max(0m, request.FreightAmount)
        };

        foreach (var itemReq in request.Items)
        {
            Product? product = null;
            if (itemReq.ProductId.HasValue)
            {
                product = await database.Products.FindAsync([itemReq.ProductId.Value], cancellationToken);
            }

            var igst = itemReq.IgstRate ?? (product?.IgstRate ?? 18m);
            var sgst = itemReq.SgstRate ?? (product?.SgstRate ?? 9m);
            var cgst = itemReq.CgstRate ?? (product?.CgstRate ?? 9m);

            if (string.Equals(quotation.GstType, "None", StringComparison.OrdinalIgnoreCase))
            {
                igst = 0m;
                sgst = 0m;
                cgst = 0m;
            }
            else if (string.Equals(quotation.GstType, "IGST", StringComparison.OrdinalIgnoreCase))
            {
                sgst = 0m;
                cgst = 0m;
            }
            else
            {
                igst = 0m;
            }

            var line = new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                ProductId = product?.Id,
                Description = string.IsNullOrWhiteSpace(itemReq.Description) ? (product?.Name ?? "Item") : itemReq.Description.Trim(),
                HsnSac = NullIfWhiteSpace(itemReq.HsnSac ?? product?.HsnSac),
                Specification = NullIfWhiteSpace(itemReq.Specification ?? product?.Description),
                Quantity = Math.Max(0.001m, itemReq.Quantity),
                Unit = string.IsNullOrWhiteSpace(itemReq.Unit) ? (product?.Unit ?? "pcs") : itemReq.Unit.Trim(),
                Rate = Math.Max(0m, itemReq.Rate),
                IgstRate = igst,
                SgstRate = sgst,
                CgstRate = cgst
            };

            quotation.Items.Add(line);
        }

        QuotationCalculator.Recalculate(quotation);

        // Note: Adding a Quotation NEVER touches Product.QuantityOnHand or Product.TotalSold.
        database.Quotations.Add(quotation);
        await database.SaveChangesAsync(cancellationToken);

        var saved = await GetQuotationGraph(quotation.Id, tracking: false, cancellationToken);
        return CreatedAtAction(nameof(GetQuotation), new { id = quotation.Id }, ToDetailDto(saved!));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(QuotationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<QuotationDetailDto>> UpdateQuotation(
        Guid id,
        QuotationRequest request,
        CancellationToken cancellationToken)
    {
        var quotation = await GetQuotationGraph(id, tracking: true, cancellationToken);
        if (quotation is null)
        {
            return NotFound();
        }

        if (request.Items.Count == 0)
        {
            return ValidationError(nameof(request.Items), "At least one item is required in the quotation.");
        }

        Customer? customer = null;
        if (request.CustomerId.HasValue)
        {
            customer = await database.Customers.FindAsync([request.CustomerId.Value], cancellationToken);
            if (customer is null)
            {
                return ValidationError(nameof(request.CustomerId), "Selected customer does not exist.");
            }
        }

        var customerName = (customer?.CompanyName?.Trim().Length > 0 ? customer.CompanyName : customer?.ContactName)
            ?? request.CustomerName?.Trim()
            ?? quotation.CustomerName;

        quotation.CustomerId = customer?.Id ?? request.CustomerId;
        quotation.CustomerName = customerName;
        quotation.CustomerPhone = NullIfWhiteSpace(customer?.Phone ?? request.CustomerPhone);
        quotation.CustomerEmail = NullIfWhiteSpace(customer?.Email ?? request.CustomerEmail);
        quotation.CustomerAddress = NullIfWhiteSpace(customer?.Address ?? request.CustomerAddress);
        quotation.CustomerGstNumber = NullIfWhiteSpace(customer?.GstNumber ?? request.CustomerGstNumber);
        quotation.QuotationDate = request.QuotationDate;
        quotation.ValidUntilDate = request.ValidUntilDate ?? quotation.ValidUntilDate;
        if (WorkflowValues.TryNormalize(request.Status, WorkflowValues.QuotationStatuses, out var normStatus))
        {
            quotation.Status = normStatus;
        }
        quotation.GstType = NullIfWhiteSpace(request.GstType) ?? "IGST";
        quotation.Notes = NullIfWhiteSpace(request.Notes);
        quotation.TermsAndConditions = NullIfWhiteSpace(request.TermsAndConditions) ?? quotation.TermsAndConditions;
        quotation.DiscountAmount = Math.Max(0m, request.DiscountAmount);
        quotation.FreightAmount = Math.Max(0m, request.FreightAmount);

        // Replace existing line items
        database.QuotationItems.RemoveRange(quotation.Items);
        quotation.Items.Clear();

        foreach (var itemReq in request.Items)
        {
            Product? product = null;
            if (itemReq.ProductId.HasValue)
            {
                product = await database.Products.FindAsync([itemReq.ProductId.Value], cancellationToken);
            }

            var igst = itemReq.IgstRate ?? (product?.IgstRate ?? 18m);
            var sgst = itemReq.SgstRate ?? (product?.SgstRate ?? 9m);
            var cgst = itemReq.CgstRate ?? (product?.CgstRate ?? 9m);

            if (string.Equals(quotation.GstType, "None", StringComparison.OrdinalIgnoreCase))
            {
                igst = 0m;
                sgst = 0m;
                cgst = 0m;
            }
            else if (string.Equals(quotation.GstType, "IGST", StringComparison.OrdinalIgnoreCase))
            {
                sgst = 0m;
                cgst = 0m;
            }
            else
            {
                igst = 0m;
            }

            var line = new QuotationItem
            {
                Id = Guid.NewGuid(),
                QuotationId = quotation.Id,
                ProductId = product?.Id,
                Description = string.IsNullOrWhiteSpace(itemReq.Description) ? (product?.Name ?? "Item") : itemReq.Description.Trim(),
                HsnSac = NullIfWhiteSpace(itemReq.HsnSac ?? product?.HsnSac),
                Specification = NullIfWhiteSpace(itemReq.Specification ?? product?.Description),
                Quantity = Math.Max(0.001m, itemReq.Quantity),
                Unit = string.IsNullOrWhiteSpace(itemReq.Unit) ? (product?.Unit ?? "pcs") : itemReq.Unit.Trim(),
                Rate = Math.Max(0m, itemReq.Rate),
                IgstRate = igst,
                SgstRate = sgst,
                CgstRate = cgst
            };

            database.QuotationItems.Add(line);
            quotation.Items.Add(line);
        }

        QuotationCalculator.Recalculate(quotation);

        // Updating a quotation NEVER adjusts inventory.
        await database.SaveChangesAsync(cancellationToken);

        var saved = await GetQuotationGraph(quotation.Id, tracking: false, cancellationToken);
        return Ok(ToDetailDto(saved!));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteQuotation(Guid id, CancellationToken cancellationToken)
    {
        var quotation = await database.Quotations
            .Include(q => q.Items)
            .SingleOrDefaultAsync(q => q.Id == id, cancellationToken);

        if (quotation is null)
        {
            return NotFound();
        }

        // Deleting quotation does not adjust inventory since it never deducted it.
        database.Quotations.Remove(quotation);
        await database.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/convert-to-order")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailDto>> ConvertToOrder(
        Guid id,
        CancellationToken cancellationToken)
    {
        var quotation = await database.Quotations
            .Include(q => q.Items)
            .ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(q => q.Id == id, cancellationToken);

        if (quotation is null)
        {
            return NotFound();
        }

        if (quotation.ConvertedSalesOrderId.HasValue)
        {
            return ValidationError(nameof(id), "This quotation has already been converted to an order.");
        }

        // Ensure customer exists
        Guid customerId;
        if (quotation.CustomerId.HasValue)
        {
            customerId = quotation.CustomerId.Value;
        }
        else
        {
            // Check if existing customer matches phone number
            Customer? existingCustomer = null;
            if (!string.IsNullOrWhiteSpace(quotation.CustomerPhone))
            {
                existingCustomer = await database.Customers
                    .FirstOrDefaultAsync(c => c.Phone == quotation.CustomerPhone, cancellationToken);
            }

            if (existingCustomer is not null)
            {
                customerId = existingCustomer.Id;
            }
            else
            {
                // Create a customer record from quotation contact info
                var newCustomer = new Customer
                {
                    Id = Guid.NewGuid(),
                    CustomerCode = $"CUS-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
                    ContactName = quotation.CustomerName,
                    CompanyName = quotation.CustomerName,
                    Phone = quotation.CustomerPhone ?? "N/A",
                    Email = quotation.CustomerEmail,
                    Address = quotation.CustomerAddress,
                    GstNumber = quotation.CustomerGstNumber,
                    IsActive = true
                };
                database.Customers.Add(newCustomer);
                customerId = newCustomer.Id;
            }
        }

        // Create SalesOrder
        var existingOrderNumbers = await database.SalesOrders.AsNoTracking()
            .Select(order => order.OrderNumber)
            .ToListAsync(cancellationToken);

        var isNonGst = string.Equals(quotation.GstType, "None", StringComparison.OrdinalIgnoreCase);
        var orderId = Guid.NewGuid();
        var orderNumber = isNonGst ? $"ORD-{orderId.ToString()[..8].ToUpperInvariant()}" : GetNextOrderNumber(DateOnly.FromDateTime(DateTime.UtcNow), existingOrderNumbers);

        var order = new SalesOrder
        {
            Id = orderId,
            OrderNumber = orderNumber,
            CustomerId = customerId,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = "Pending",
            Notes = quotation.Notes ?? $"Converted from Quotation {quotation.QuotationNumber}",
            GstType = quotation.GstType,
            DiscountAmount = quotation.DiscountAmount,
            FreightAmount = quotation.FreightAmount
        };

        database.SalesOrders.Add(order);

        foreach (var item in quotation.Items)
        {
            var orderItem = new SalesOrderItem
            {
                Id = Guid.NewGuid(),
                SalesOrderId = order.Id,
                ProductId = item.ProductId,
                Description = item.Description,
                HsnSac = item.HsnSac,
                Specification = item.Specification,
                Quantity = item.Quantity,
                Unit = item.Unit,
                Rate = item.Rate,
                IgstRate = item.IgstRate,
                SgstRate = item.SgstRate,
                CgstRate = item.CgstRate,
                LineSubtotal = item.LineSubtotal,
                TaxAmount = item.TaxAmount,
                LineTotal = item.LineTotal
            };
            database.SalesOrderItems.Add(orderItem);
            order.Items.Add(orderItem);
        }

        OrderCalculator.Recalculate(order);

        // Mark quotation converted
        quotation.Status = "Converted";
        quotation.ConvertedSalesOrderId = order.Id;

        // When SaveChangesAsync is called on SalesOrder, standard order stock deduction triggers automatically!
        await database.SaveChangesAsync(cancellationToken);

        var savedOrder = await database.SalesOrders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .SingleOrDefaultAsync(o => o.Id == order.Id, cancellationToken);

        return Ok(OrdersController.ToDetailDto(savedOrder!));
    }

    private async Task<Quotation?> GetQuotationGraph(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = database.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items)
            .ThenInclude(i => i.Product)
            .AsSplitQuery();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(q => q.Id == id, cancellationToken);
    }

    private static QuotationSummaryDto ToSummaryDto(Quotation quotation) =>
        new(
            quotation.Id,
            quotation.QuotationNumber,
            quotation.CustomerId,
            quotation.CustomerName,
            quotation.CustomerPhone,
            quotation.QuotationDate,
            quotation.ValidUntilDate,
            quotation.Status,
            quotation.GstType,
            quotation.Subtotal,
            quotation.DiscountAmount,
            quotation.FreightAmount,
            quotation.TaxAmount,
            quotation.GrandTotal,
            quotation.Items.Count,
            quotation.ConvertedSalesOrderId,
            quotation.CreatedAtUtc,
            quotation.UpdatedAtUtc,
            quotation.Items.Select(ToItemDto).ToList()
        );

    private static QuotationDetailDto ToDetailDto(Quotation quotation, CompanyProfileDto? company = null) =>
        new(
            quotation.Id,
            quotation.QuotationNumber,
            quotation.CustomerId,
            quotation.CustomerName,
            quotation.CustomerPhone,
            quotation.CustomerEmail,
            quotation.CustomerAddress,
            quotation.CustomerGstNumber,
            quotation.QuotationDate,
            quotation.ValidUntilDate,
            quotation.Status,
            quotation.GstType,
            quotation.Subtotal,
            quotation.DiscountAmount,
            quotation.FreightAmount,
            quotation.TaxAmount,
            quotation.GrandTotal,
            quotation.Notes,
            quotation.TermsAndConditions,
            quotation.ConvertedSalesOrderId,
            quotation.CreatedAtUtc,
            quotation.UpdatedAtUtc,
            quotation.Items.Select(ToItemDto).ToList(),
            company
        );

    private static QuotationItemDto ToItemDto(QuotationItem item) =>
        new(
            item.Id,
            item.ProductId,
            item.Product?.ProductCode,
            item.Description,
            item.HsnSac,
            item.Specification,
            item.Quantity,
            item.Unit,
            item.Rate,
            item.IgstRate,
            item.SgstRate,
            item.CgstRate,
            item.LineSubtotal,
            item.TaxAmount,
            item.LineTotal
        );

    private static string GetNextQuotationNumber(DateOnly quotationDate, IEnumerable<string> existingNumbers)
    {
        var financialYear = GetFinancialYear(quotationDate);
        var prefix = "QT-";
        var suffix = $"/{financialYear}";

        var maxNumber = existingNumbers
            .Where(num => num.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && num.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(num =>
            {
                var core = num[prefix.Length..^suffix.Length];
                return int.TryParse(core, out var val) ? val : (int?)null;
            })
            .Where(val => val.HasValue)
            .Select(val => val!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{maxNumber + 1:D2}/{financialYear}";
    }

    private static string GetNextOrderNumber(DateOnly orderDate, IEnumerable<string> orderNumbers)
    {
        var financialYear = GetFinancialYear(orderDate);
        var suffix = $"/{financialYear}";
        var maxNumber = orderNumbers
            .Where(num => num.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(num =>
            {
                var seqText = num[..^suffix.Length];
                return int.TryParse(seqText, out var val) ? val : (int?)null;
            })
            .Where(val => val.HasValue)
            .Select(val => val!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return $"{maxNumber + 1:D2}/{financialYear}";
    }

    private static string GetFinancialYear(DateOnly date)
    {
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{startYear % 100:D2}-{(startYear + 1) % 100:D2}";
    }

    private const string DefaultTerms =
        "1. Validity: This quotation is valid for 15 days from the date of issue.\n" +
        "2. Taxes: GST as applicable at the time of invoicing.\n" +
        "3. Payment: 50% advance along with confirmed order, balance against dispatch.\n" +
        "4. Delivery: Dispatched within 7-10 working days after order confirmation.\n" +
        "5. Freight: Transport charges extra at actuals unless explicitly included.";
}
