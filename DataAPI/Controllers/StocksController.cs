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
public class StocksController : ControllerBase
{
    private readonly IRavenDbService _raven;

    public StocksController(IRavenDbService raven)
    {
        _raven = raven;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Stock>>> GetAll()
    {
        var cacheKey = CacheKeys.All(CacheKeys.Stocks);

        if (_raven.Cache.TryGetValue(cacheKey, out IReadOnlyList<Stock>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var items = await session.Query<Stock>()
            .OrderBy(x => x.ProductId)
            .ToListAsync();

        CacheResult(cacheKey, items, CacheKeys.Stocks);

        return Ok(items);
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<Stock>>> GetPaged(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string? search = null,
    [FromQuery] string? sortBy = null,
    [FromQuery] bool sortDescending = false)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var productId = search;

        var cacheKey = CacheKeys.Paged(
            CacheKeys.Stocks,
            page,
            pageSize,
            productId,
            sortBy,
            sortDescending);

        if (_raven.Cache.TryGetValue(cacheKey, out PagedResult<Stock>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var query = session.Query<Stock>();

        if (!string.IsNullOrWhiteSpace(productId))
        {
            query = query.Where(x => x.ProductId == productId);
        }

        query = ApplySorting(query, sortBy, sortDescending);

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = new PagedResult<Stock>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        CacheResult(cacheKey, result, CacheKeys.Stocks);

        return Ok(result);
    }

    private static IRavenQueryable<Stock> ApplySorting(
        IRavenQueryable<Stock> query,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            nameof(Stock.ProductId) => sortDescending
                ? query.OrderByDescending(x => x.ProductId)
                : query.OrderBy(x => x.ProductId),

            nameof(Stock.Quantity) => sortDescending
                ? query.OrderByDescending(x => x.Quantity)
                : query.OrderBy(x => x.Quantity),

            nameof(Stock.Reserve) => sortDescending
                ? query.OrderByDescending(x => x.Reserve)
                : query.OrderBy(x => x.Reserve),

            nameof(Stock.Transit) => sortDescending
                ? query.OrderByDescending(x => x.Transit)
                : query.OrderBy(x => x.Transit),

            _ => sortDescending
                ? query.OrderByDescending(x => x.ProductId)
                : query.OrderBy(x => x.ProductId)
        };
    }

    [HttpGet("count")]
    public async Task<ActionResult<int>> GetCount()
    {
        var cacheKey = CacheKeys.Count(CacheKeys.Stocks);

        if (_raven.Cache.TryGetValue(cacheKey, out int cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var count = await session.Query<Stock>().CountAsync();

        CacheResult(cacheKey, count, CacheKeys.Stocks);

        return Ok(count);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Stock>> GetById(string id)
    {
        var cacheKey = CacheKeys.ById(CacheKeys.Stocks, id);

        if (_raven.Cache.TryGetValue(cacheKey, out Stock? cached))
        {
            return cached is null ? NotFound() : Ok(cached);
        }

        using var session = _raven.OpenSession();

        var stock = await session.LoadAsync<Stock>(id);

        CacheResult(cacheKey, stock, CacheKeys.Stocks);

        return stock is null ? NotFound() : Ok(stock);
    }

    [HttpPost]
    public async Task<ActionResult<Stock>> Create(Stock stock)
    {
        using var session = _raven.OpenSession();

        var product = await session.LoadAsync<Product>(stock.ProductId);

        if (product is null)
        {
            return BadRequest($"Product '{stock.ProductId}' does not exist.");
        }

        stock.Id = Guid.NewGuid().ToString();

        await session.StoreAsync(stock, stock.Id);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Stocks);

        return CreatedAtAction(nameof(GetById), new { id = stock.Id }, stock);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Stock stock)
    {
        if (id != stock.Id)
        {
            return BadRequest("Route ID and body ID must match.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Stock>(id);

        if (existing is null)
        {
            return NotFound();
        }

        var product = await session.LoadAsync<Product>(stock.ProductId);

        if (product is null)
        {
            return BadRequest($"Product '{stock.ProductId}' does not exist.");
        }

        existing.ProductId = stock.ProductId;
        existing.Quantity = stock.Quantity;
        existing.Reserve = stock.Reserve;
        existing.Transit = stock.Transit;

        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Stocks);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Stock>(id);

        if (existing is null)
        {
            return NotFound();
        }

        session.Delete(existing);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Stocks);

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