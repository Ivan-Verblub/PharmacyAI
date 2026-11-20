using DataAPI.Common;
using DataAPI.Infrastructure;
using DataAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;

namespace DataAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FlowsController : ControllerBase
{
    private readonly IRavenDbService _raven;

    public FlowsController(IRavenDbService raven)
    {
        _raven = raven;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Flow>>> GetAll()
    {
        var cacheKey = CacheKeys.All(CacheKeys.Flows);

        if (_raven.Cache.TryGetValue(cacheKey, out IReadOnlyList<Flow>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var items = await session.Query<Flow>()
            .OrderByDescending(x => x.Date)
            .ToListAsync();

        CacheResult(cacheKey, items, CacheKeys.Flows);

        return Ok(items);
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<Flow>>> GetPaged(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string? search = null,
    [FromQuery] string? sortBy = null,
    [FromQuery] bool sortDescending = false,
    [FromQuery] string? branchId = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var productId = search;

        var cacheKey = CacheKeys.Paged(
            CacheKeys.Flows,
            page,
            pageSize,
            $"{productId}|branch={branchId}",
            sortBy,
            sortDescending);

        if (_raven.Cache.TryGetValue(cacheKey, out PagedResult<Flow>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var query = session.Query<Flow>();

        if (!string.IsNullOrWhiteSpace(productId))
        {
            query = query.Where(x => x.ProductId == productId);
        }

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(x => x.BranchId == branchId);
        }

        query = ApplySorting(query, sortBy, sortDescending);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = new PagedResult<Flow>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        CacheResult(cacheKey, result, CacheKeys.Flows);

        return Ok(result);
    }

    private static IRavenQueryable<Flow> ApplySorting(
    IRavenQueryable<Flow> query,
    string? sortBy,
    bool sortDescending)
    {
        return sortBy switch
        {
            nameof(Flow.Date) => sortDescending
                ? query.OrderByDescending(x => x.Date)
                : query.OrderBy(x => x.Date),

            nameof(Flow.BranchId) => sortDescending
                ? query.OrderByDescending(x => x.BranchId)
                : query.OrderBy(x => x.BranchId),

            nameof(Flow.ProductId) => sortDescending
                ? query.OrderByDescending(x => x.ProductId)
                : query.OrderBy(x => x.ProductId),

            nameof(Flow.QuantityIncome) => sortDescending
                ? query.OrderByDescending(x => x.QuantityIncome)
                : query.OrderBy(x => x.QuantityIncome),

            nameof(Flow.QuantityOutcome) => sortDescending
                ? query.OrderByDescending(x => x.QuantityOutcome)
                : query.OrderBy(x => x.QuantityOutcome),

            _ => sortDescending
                ? query.OrderByDescending(x => x.Date)
                : query.OrderBy(x => x.Date)
        };
    }

    [HttpGet("count")]
    public async Task<ActionResult<int>> GetCount()
    {
        var cacheKey = CacheKeys.Count(CacheKeys.Flows);

        if (_raven.Cache.TryGetValue(cacheKey, out int cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var count = await session.Query<Flow>().CountAsync();

        CacheResult(cacheKey, count, CacheKeys.Flows);

        return Ok(count);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Flow>> GetById(string id)
    {
        var cacheKey = CacheKeys.ById(CacheKeys.Flows, id);

        if (_raven.Cache.TryGetValue(cacheKey, out Flow? cached))
        {
            return cached is null ? NotFound() : Ok(cached);
        }

        using var session = _raven.OpenSession();

        var flow = await session.LoadAsync<Flow>(id);

        CacheResult(cacheKey, flow, CacheKeys.Flows);

        return flow is null ? NotFound() : Ok(flow);
    }

    [HttpPost]
    public async Task<ActionResult<Flow>> Create(Flow flow)
    {
        using var session = _raven.OpenSession();

        var product = await session.LoadAsync<Product>(flow.ProductId);

        if (product is null)
        {
            return BadRequest($"Product '{flow.ProductId}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(flow.BranchId))
        {
            flow.BranchId = Branch.DefaultId;
        }

        if (await session.LoadAsync<Branch>(flow.BranchId) is null)
        {
            return BadRequest($"Branch '{flow.BranchId}' does not exist.");
        }

        flow.Id = Guid.NewGuid().ToString();

        await session.StoreAsync(flow, flow.Id);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Flows);

        return CreatedAtAction(nameof(GetById), new { id = flow.Id }, flow);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Flow flow)
    {
        if (id != flow.Id)
        {
            return BadRequest("Route ID and body ID must match.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Flow>(id);

        if (existing is null)
        {
            return NotFound();
        }

        var product = await session.LoadAsync<Product>(flow.ProductId);

        if (product is null)
        {
            return BadRequest($"Product '{flow.ProductId}' does not exist.");
        }

        existing.Date = flow.Date;
        if (string.IsNullOrWhiteSpace(flow.BranchId))
        {
            flow.BranchId = Branch.DefaultId;
        }

        if (await session.LoadAsync<Branch>(flow.BranchId) is null)
        {
            return BadRequest($"Branch '{flow.BranchId}' does not exist.");
        }

        existing.ProductId = flow.ProductId;
        existing.BranchId = flow.BranchId;
        existing.QuantityIncome = flow.QuantityIncome;
        existing.QuantityOutcome = flow.QuantityOutcome;
        existing.ReserveIncome = flow.ReserveIncome;
        existing.ReserveOutcome = flow.ReserveOutcome;

        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Flows);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Flow>(id);

        if (existing is null)
        {
            return NotFound();
        }

        session.Delete(existing);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Flows);

        return NoContent();
    }

    private void CacheResult<T>(string key, T value, string entity)
    {
        _raven.Cache.Set(key, value, TimeSpan.FromMinutes(5));

        var indexKey = CacheKeys.IndexKey(entity);

        var keys = _raven.Cache.TryGetValue(indexKey, out List<string>? existingKeys)
            ? existingKeys ?? []
            : [];

        keys.Add(key);

        _raven.Cache.Set(indexKey, keys, TimeSpan.FromMinutes(5));
    }
}