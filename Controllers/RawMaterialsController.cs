using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/rawmaterials")]
public sealed class RawMaterialsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<RawMaterialDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<RawMaterialDto>>> GetRawMaterials(
        [FromQuery] string? search,
        [FromQuery] bool includeInactive = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.RawMaterials.AsNoTracking().AsQueryable();
        
        if (!includeInactive)
        {
            query = query.Where(rm => rm.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(rm => rm.Name.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(rm => rm.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<RawMaterialDto>(items.Select(rm => rm.ToDto()).ToList(), page, pageSize, totalCount));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RawMaterialDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RawMaterialDto>> GetRawMaterial(Guid id, CancellationToken cancellationToken)
    {
        var rm = await database.RawMaterials.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return rm is null ? NotFound() : Ok(rm.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(RawMaterialDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RawMaterialDto>> CreateRawMaterial(RawMaterialRequest request, CancellationToken cancellationToken)
    {
        var rm = new RawMaterial
        {
            Name = request.Name.Trim(),
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "kg" : request.Unit.Trim(),
            Specification = string.IsNullOrWhiteSpace(request.Specification) ? null : request.Specification.Trim(),
            TotalStock = request.Quantity
        };
        
        database.RawMaterials.Add(rm);
        await database.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetRawMaterial), new { rm.Id }, rm.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(RawMaterialDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RawMaterialDto>> UpdateRawMaterial(Guid id, RawMaterialRequest request, CancellationToken cancellationToken)
    {
        var rm = await database.RawMaterials.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (rm is null)
        {
            return NotFound();
        }

        rm.Name = request.Name.Trim();
        rm.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "kg" : request.Unit.Trim();
        rm.Specification = string.IsNullOrWhiteSpace(request.Specification) ? null : request.Specification.Trim();
        // An unchanged Stock field must not replace a newer balance.
        var stock = request.OriginalQuantity == request.Quantity ? rm.TotalStock : request.Quantity;
        var updatedStock = stock + request.AddStock;
        if (request.AddStock < 0 || updatedStock > 999999999999999m || request.AddStock % 0.001m != 0)
        {
            return ValidationError(nameof(request.AddStock), "Enter a non-negative stock quantity with at most 3 decimal places within the supported range.");
        }
        rm.TotalStock = updatedStock;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Stock changed while saving. Refresh and submit again.");
        }
        return Ok(rm.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRawMaterial(Guid id, CancellationToken cancellationToken)
    {
        var rm = await database.RawMaterials.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (rm is null)
        {
            return NotFound();
        }

        rm.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
