using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

/// <summary>
/// Provides the inventory ledger and monitoring views. Product quantities are
/// changed only by recorded adjustments so there is always an audit trail.
/// </summary>
[Route("api/inventory")]
public sealed class InventoryController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(PagedResult<InventorySummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InventorySummaryDto>>> GetSummary(
        [FromQuery] string? search,
        [FromQuery] bool lowStockOnly = false,
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

        if (lowStockOnly)
        {
            query = query.Where(product => product.QuantityOnHand <= product.ReorderLevel);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(product =>
                product.Name.ToLower().Contains(term) ||
                product.ProductCode.ToLower().Contains(term) ||
                product.Category.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(product => product.QuantityOnHand <= product.ReorderLevel ? 0 : 1)
            .ThenBy(product => product.QuantityOnHand)
            .ThenBy(product => product.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new InventorySummaryDto(
                product.Id,
                product.ProductCode,
                product.Name,
                product.Category,
                product.Unit,
                product.QuantityOnHand,
                product.ReorderLevel,
                product.QuantityOnHand <= product.ReorderLevel,
                product.QuantityOnHand <= 0m,
                product.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<InventorySummaryDto>(items, page, pageSize, totalCount));
    }

    [HttpGet("movements")]
    [ProducesResponseType(typeof(PagedResult<StockMovementDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StockMovementDto>>> GetMovements(
        [FromQuery] Guid? productId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.StockMovements.AsNoTracking().AsQueryable();
        if (productId.HasValue)
        {
            query = query.Where(movement => movement.ProductId == productId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(movement => movement.OccurredAtUtc)
            .ThenByDescending(movement => movement.CreatedAtUtc)
            .ThenByDescending(movement => movement.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(movement => new StockMovementDto(
                movement.Id,
                movement.ProductId,
                movement.Product.ProductCode,
                movement.Product.Name,
                movement.Product.Unit,
                movement.QuantityChange,
                movement.BalanceAfter,
                movement.MovementType,
                movement.Reason,
                movement.Reference,
                movement.OccurredAtUtc,
                movement.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<StockMovementDto>(items, page, pageSize, totalCount));
    }

    [HttpPost("products/{productId:guid}/adjustments")]
    [ProducesResponseType(typeof(StockAdjustmentResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockAdjustmentResultDto>> AdjustStock(
        Guid productId,
        StockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.QuantityChange == 0m)
        {
            return ValidationError(nameof(request.QuantityChange), "Quantity change cannot be zero.");
        }

        if (!TryNormalizeMovementType(request.MovementType, out var movementType))
        {
            return ValidationError(nameof(request.MovementType), $"Movement type must be one of: {string.Join(", ", StockMovementTypes.All)}.");
        }

        if (movementType == StockMovementTypes.StockIn && request.QuantityChange < 0m)
        {
            return ValidationError(nameof(request.QuantityChange), "StockIn requires a positive quantity change.");
        }

        if (movementType == StockMovementTypes.StockOut && request.QuantityChange > 0m)
        {
            return ValidationError(nameof(request.QuantityChange), "StockOut requires a negative quantity change.");
        }

        var occurredAtUtc = request.OccurredAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        if (occurredAtUtc > DateTime.UtcNow.AddMinutes(5))
        {
            return ValidationError(nameof(request.OccurredAtUtc), "A stock movement cannot be recorded in the future.");
        }

        var product = await database.Products.SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (!product.IsActive)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "This product is inactive.",
                Detail = "Reactivate the product before recording stock movements.",
            });
        }

        var balanceAfter = product.QuantityOnHand + request.QuantityChange;
        if (balanceAfter < 0m)
        {
            return ValidationError(nameof(request.QuantityChange), "This adjustment would make the available stock negative.");
        }

        var movement = new StockMovement
        {
            ProductId = product.Id,
            QuantityChange = request.QuantityChange,
            BalanceAfter = balanceAfter,
            MovementType = movementType,
            Reason = Null(request.Reason),
            Reference = Null(request.Reference),
            OccurredAtUtc = occurredAtUtc,
        };
        product.QuantityOnHand = balanceAfter;
        database.StockMovements.Add(movement);

        try
        {
            // SaveChanges wraps the product update and immutable ledger insert in a
            // single relational transaction. Product.RowVersion rejects stale,
            // concurrent stock edits instead of silently overwriting a balance.
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Stock changed while this adjustment was being saved.",
                Detail = "Refresh the product quantity and submit the adjustment again.",
            });
        }

        return CreatedAtAction(
            nameof(GetMovements),
            new { productId = product.Id },
            new StockAdjustmentResultDto(ToDto(movement, product), product.ToDto()));
    }

    private static StockMovementDto ToDto(StockMovement movement, Product product) => new(
        movement.Id,
        product.Id,
        product.ProductCode,
        product.Name,
        product.Unit,
        movement.QuantityChange,
        movement.BalanceAfter,
        movement.MovementType,
        movement.Reason,
        movement.Reference,
        movement.OccurredAtUtc,
        movement.CreatedAtUtc);

    private static bool TryNormalizeMovementType(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var type in StockMovementTypes.All)
        {
            if (string.Equals(type, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                normalized = type;
                return true;
            }
        }

        return false;
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
