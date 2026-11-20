using DataAPI.Common;
using DataAPI.Infrastructure;
using DataAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Raven.Client.Documents;

namespace DataAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BranchesController : ControllerBase
{
    private readonly IRavenDbService _raven;

    public BranchesController(IRavenDbService raven)
    {
        _raven = raven;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Branch>>> GetAll()
    {
        var cacheKey = CacheKeys.All(CacheKeys.Branches);

        if (_raven.Cache.TryGetValue(cacheKey, out IReadOnlyList<Branch>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var items = await session.Query<Branch>()
            .OrderBy(x => x.Name)
            .ToListAsync();

        CacheResult(cacheKey, items);

        return Ok(items);
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<Branch>>> GetPaged(
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

        var items = await session.Query<Branch>().ToListAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            items = items
                .Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                         || x.Code.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        Func<Branch, object> key = sortBy switch
        {
            nameof(Branch.Code) => x => x.Code,
            nameof(Branch.Address) => x => x.Address,
            nameof(Branch.IsActive) => x => x.IsActive,
            _ => x => x.Name
        };

        items = (sortDescending ? items.OrderByDescending(key) : items.OrderBy(key)).ToList();

        return Ok(new PagedResult<Branch>
        {
            Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            TotalCount = items.Count,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Branch>> GetById(string id)
    {
        using var session = _raven.OpenSession();

        var branch = await session.LoadAsync<Branch>(id);

        return branch is null ? NotFound() : Ok(branch);
    }

    [HttpPost]
    public async Task<ActionResult<Branch>> Create(Branch branch)
    {
        if (string.IsNullOrWhiteSpace(branch.Name))
        {
            return BadRequest("Наименование филиала обязательно.");
        }

        branch.Id = Guid.NewGuid().ToString();

        using var session = _raven.OpenSession();

        await session.StoreAsync(branch, branch.Id);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Branches);

        return Ok(branch);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Branch branch)
    {
        if (id != branch.Id)
        {
            return BadRequest("Route ID and body ID must match.");
        }

        if (string.IsNullOrWhiteSpace(branch.Name))
        {
            return BadRequest("Наименование филиала обязательно.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Branch>(id);

        if (existing is null)
        {
            return NotFound();
        }

        existing.Code = branch.Code;
        existing.Name = branch.Name;
        existing.Address = branch.Address;
        existing.IsActive = branch.IsActive;

        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Branches);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (id == Branch.DefaultId)
        {
            return Conflict("Основной филиал нельзя удалить.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Branch>(id);

        if (existing is null)
        {
            return NotFound();
        }

        var used = await session.Query<Flow>().AnyAsync(x => x.BranchId == id)
                || await session.Query<Stock>().AnyAsync(x => x.BranchId == id);

        if (used)
        {
            return Conflict("Филиал используется в движениях или остатках. Сделайте его неактивным.");
        }

        session.Delete(existing);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Branches);

        return NoContent();
    }

    private void CacheResult<T>(string key, T value)
    {
        _raven.Cache.Set(key, value, TimeSpan.FromMinutes(5));

        var indexKey = CacheKeys.IndexKey(CacheKeys.Branches);

        var keys = _raven.Cache.TryGetValue(indexKey, out List<string>? existingKeys)
            ? existingKeys ?? []
            : [];

        keys.Add(key);

        _raven.Cache.Set(indexKey, keys, TimeSpan.FromMinutes(5));
    }
}
