using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/products")]
public sealed class ProductsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetProducts(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.Products.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(product => product.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryTerm = category.Trim().ToLower();
            query = query.Where(product => product.Category.ToLower() == categoryTerm);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(product =>
                product.Name.ToLower().Contains(term) ||
                product.ProductCode.ToLower().Contains(term) ||
                product.Category.ToLower().Contains(term) ||
                (product.MeshOpening ?? string.Empty).ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderBy(product => product.Category)
            .ThenBy(product => product.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResult<ProductDto>(products.Select(product => product.ToDto()).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return product is null ? NotFound() : Ok(product.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ProductDto>> CreateProduct(ProductRequest request, CancellationToken cancellationToken)
    {
        var initialStock = request.InitialStock ?? 0m;
        var product = new Product
        {
            ProductCode = DocumentNumbers.New("PRD"),
            QuantityOnHand = initialStock,
            TotalStockAdded = initialStock,
        };
        Apply(product, request);
        database.Products.Add(product);

        // Initial stock is a real inventory movement.  Recording it in the
        // same product history used by later adjustments keeps the displayed
        // balance and its audit trail aligned from the first save.
        if (initialStock > 0m)
        {
            database.ProductTransactions.Add(new ProductTransaction
            {
                ProductId = product.Id,
                TransactionNumber = DocumentNumbers.New("PA"),
                TransactionType = "Adjustment",
                TransactionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                Quantity = initialStock,
                Amount = 0m,
                PaymentStatus = "Not Applicable",
                Notes = "Initial stock",
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProduct), new { product.Id }, product.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, ProductRequest request, CancellationToken cancellationToken)
    {
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (request.InitialStock.HasValue)
        {
            return ValidationError(
                nameof(request.InitialStock),
                "Use the stock adjustment action to increase or decrease an existing product's stock.");
        }

        Apply(product, request);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(product.ToDto());
    }

    [HttpPost("{id:guid}/adjustments")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> AdjustStock(Guid id, [FromBody] StockAdjustmentRequest request, CancellationToken cancellationToken)
    {
        const decimal maximumQuantity = 999_999_999_999_999m;
        if (request.QuantityChange == 0m)
        {
            return ValidationError(nameof(request.QuantityChange), "Enter a non-zero quantity to increase or decrease stock.");
        }

        if (request.QuantityChange is < -maximumQuantity or > maximumQuantity ||
            !FitsScale(request.QuantityChange, 0.001m))
        {
            return ValidationError(nameof(request.QuantityChange), "Stock quantity must have at most 3 decimal places and be within the supported range.");
        }

        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (request.QuantityChange < 0m && product.QuantityOnHand < -request.QuantityChange)
        {
            return ValidationError(
                nameof(request.QuantityChange),
                $"Cannot decrease {product.Name} below zero. Available stock is {product.QuantityOnHand:0.###} {product.Unit}.");
        }

        product.QuantityOnHand += request.QuantityChange;

        if (request.QuantityChange > 0)
        {
            product.TotalStockAdded += request.QuantityChange;
        }

        // Keep a single lightweight inventory ledger for the simple
        // increase/decrease flow.  The sign of Quantity records the direction
        // without asking the user for a separate stock-details form.
        database.ProductTransactions.Add(new ProductTransaction
        {
            ProductId = product.Id,
            TransactionNumber = DocumentNumbers.New("PA"),
            TransactionType = "Adjustment",
            TransactionDate = DateOnly.FromDateTime((request.OccurredAtUtc ?? DateTime.UtcNow).ToUniversalTime()),
            Quantity = request.QuantityChange,
            Amount = 0m,
            PaymentStatus = "Not Applicable",
            Notes = BuildAdjustmentNotes(request),
        });

        await database.SaveChangesAsync(cancellationToken);
        return Ok(product.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        // Product references on historical order items are intentionally preserved.
        product.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static void Apply(Product product, ProductRequest request)
    {
        product.Name = request.Name.Trim();
        product.HsnSac = request.HsnSac.Trim();
        product.Category = request.Category.Trim();
        product.MeshType = Null(request.MeshType);
        product.MeshOpening = Null(request.MeshOpening);
        product.WireDiameter = Null(request.WireDiameter);
        product.Width = request.Width;
        product.Length = request.Length;
        product.Unit = request.Unit.Trim();
        product.Rate = request.Rate;
        product.IgstRate = request.IgstRate; product.SgstRate = request.SgstRate; product.CgstRate = request.CgstRate;
        product.ReorderLevel = request.ReorderLevel;
        product.Description = Null(request.Description);
        product.IsActive = request.IsActive;
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool FitsScale(decimal value, decimal smallestUnit) =>
        value % smallestUnit == 0m;

    private static string BuildAdjustmentNotes(StockAdjustmentRequest request)
    {
        var details = new List<string>
        {
            request.QuantityChange > 0m ? "Stock increased" : "Stock decreased",
        };
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            details.Add(request.Reason.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.Reference))
        {
            details.Add($"Reference: {request.Reference.Trim()}");
        }

        return string.Join(". ", details);
    }
}
