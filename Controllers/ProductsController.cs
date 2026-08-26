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
        if (initialStock > 0m)
        {
            product.StockMovements.Add(new StockMovement
            {
                QuantityChange = initialStock,
                BalanceAfter = initialStock,
                MovementType = StockMovementTypes.OpeningBalance,
                Reason = "Opening stock recorded when the product was created.",
            });
        }

        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetProduct), new { product.Id }, product.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> UpdateProduct(Guid id, ProductRequest request, CancellationToken cancellationToken)
    {
        if (request.InitialStock.HasValue)
        {
            return ValidationError(nameof(request.InitialStock), "Initial stock can only be set while creating a product. Use a stock adjustment to change current stock.");
        }

        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        Apply(product, request);
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
        product.Category = request.Category.Trim();
        product.MeshType = Null(request.MeshType);
        product.MeshOpening = Null(request.MeshOpening);
        product.WireDiameter = Null(request.WireDiameter);
        product.Width = request.Width;
        product.Length = request.Length;
        product.Unit = request.Unit.Trim();
        product.Rate = request.Rate;
        product.GstRate = request.GstRate;
        product.ReorderLevel = request.ReorderLevel;
        product.Description = Null(request.Description);
        product.IsActive = request.IsActive;
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
