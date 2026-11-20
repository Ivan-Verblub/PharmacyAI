using DataAPI.Common;
using DataAPI.Infrastructure;
using DataAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Raven.Client.Documents;

namespace DataAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UnitsController : ControllerBase
{
    private readonly IRavenDbService _raven;

    public UnitsController(IRavenDbService raven)
    {
        _raven = raven;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Unit>>> GetAll()
    {
        var cacheKey = CacheKeys.All(CacheKeys.Units);

        if (_raven.Cache.TryGetValue(cacheKey, out IReadOnlyList<Unit>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var items = await session.Query<Unit>()
            .OrderBy(x => x.Name)
            .ToListAsync();

        CacheResult(cacheKey, items);

        return Ok(items);
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<Unit>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        using var session = _raven.OpenSession();

        var items = await session.Query<Unit>().ToListAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            items = items
                .Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                         || x.ShortName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Func<Unit, object> key = sortBy switch
        {
            nameof(Unit.Code) => x => x.Code,
            nameof(Unit.ShortName) => x => x.ShortName,
            _ => x => x.Name
        };

        items = (sortDescending ? items.OrderByDescending(key) : items.OrderBy(key)).ToList();

        return Ok(new PagedResult<Unit>
        {
            Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = items.Count,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Unit>> GetById(string id)
    {
        using var session = _raven.OpenSession();

        var unit = await session.LoadAsync<Unit>(id);

        return unit is null ? NotFound() : Ok(unit);
    }

    [HttpPost]
    public async Task<ActionResult<Unit>> Create(Unit unit)
    {
        if (string.IsNullOrWhiteSpace(unit.Name))
        {
            return BadRequest("Наименование единицы измерения обязательно.");
        }

        unit.Id = Guid.NewGuid().ToString();

        using var session = _raven.OpenSession();

        await session.StoreAsync(unit, unit.Id);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Units);

        return Ok(unit);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Unit unit)
    {
        if (id != unit.Id)
        {
            return BadRequest("Route ID and body ID must match.");
        }

        if (string.IsNullOrWhiteSpace(unit.Name))
        {
            return BadRequest("Наименование единицы измерения обязательно.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Unit>(id);

        if (existing is null)
        {
            return NotFound();
        }

        existing.Code = unit.Code;
        existing.Name = unit.Name;
        existing.ShortName = unit.ShortName;

        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Units, CacheKeys.Products);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (id == Unit.DefaultId)
        {
            return Conflict("Базовую единицу «шт.» нельзя удалить.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Unit>(id);

        if (existing is null)
        {
            return NotFound();
        }

        var used = await session.Query<Product>()
            .AnyAsync(x => x.UnitId == id || x.PurchaseUnitId == id);

        if (used)
        {
            return Conflict("Единица измерения используется в товарах.");
        }

        session.Delete(existing);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Units);

        return NoContent();
    }

    private void CacheResult<T>(string key, T value)
    {
        _raven.Cache.Set(key, value, TimeSpan.FromMinutes(5));

        var indexKey = CacheKeys.IndexKey(CacheKeys.Units);

        var keys = _raven.Cache.TryGetValue(indexKey, out List<string>? existingKeys)
            ? existingKeys ?? []
            : [];

        keys.Add(key);

        _raven.Cache.Set(indexKey, keys, TimeSpan.FromMinutes(5));
    }
}
