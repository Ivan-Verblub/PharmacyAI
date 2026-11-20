using DataAPI.Common;
using DataAPI.Infrastructure;
using DataAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace DataAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IRavenDbService _raven;

    public ProductsController(IRavenDbService raven)
    {
        _raven = raven;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Product>>> GetAll()
    {
        var cacheKey = CacheKeys.All(CacheKeys.Products);

        if (_raven.Cache.TryGetValue(cacheKey, out IReadOnlyList<Product>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var items = await session.Query<Product>()
            .OrderBy(x => x.Name)
            .ToListAsync();

        CacheResult(cacheKey, items, CacheKeys.Products);

        return Ok(items);
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<Product>>> GetPaged(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20,
    [FromQuery] string? search = null,
    [FromQuery] string? sortBy = null,
    [FromQuery] bool sortDescending = false)
    {
        using var session = _raven.OpenSession();

        var items = await session
            .Query<Product>()
            .OrderBy(x => x.Name)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            items = items
                .Where(x => x.Name.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        items = sortBy switch
        {
            nameof(Product.Name) => sortDescending
                ? items.OrderByDescending(x => x.Name).ToList()
                : items.OrderBy(x => x.Name).ToList(),

            nameof(Product.DeliveryDays) => sortDescending
                ? items.OrderByDescending(x => x.DeliveryDays).ToList()
                : items.OrderBy(x => x.DeliveryDays).ToList(),

            nameof(Product.StorageLife) => sortDescending
                ? items.OrderByDescending(x => x.StorageLife).ToList()
                : items.OrderBy(x => x.StorageLife).ToList(),

            nameof(Product.MinPurchaseQuantity) => sortDescending
                ? items.OrderByDescending(x => x.MinPurchaseQuantity).ToList()
                : items.OrderBy(x => x.MinPurchaseQuantity).ToList(),

            nameof(Product.UnitId) => sortDescending
                ? items.OrderByDescending(x => x.UnitId).ToList()
                : items.OrderBy(x => x.UnitId).ToList(),

            _ => sortDescending
                ? items.OrderByDescending(x => x.Name).ToList()
                : items.OrderBy(x => x.Name).ToList()
        };

        var totalCount = items.Count;

        var pagedItems = items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new PagedResult<Product>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("count")]
    public async Task<ActionResult<int>> GetCount()
    {
        var cacheKey = CacheKeys.Count(CacheKeys.Products);

        if (_raven.Cache.TryGetValue(cacheKey, out int cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var count = await session.Query<Product>().CountAsync();

        CacheResult(cacheKey, count, CacheKeys.Products);

        return Ok(count);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Product>> GetById(string id)
    {
        var cacheKey = CacheKeys.ById(CacheKeys.Products, id);

        if (_raven.Cache.TryGetValue(cacheKey, out Product? cached))
        {
            return cached is null ? NotFound() : Ok(cached);
        }

        using var session = _raven.OpenSession();

        var product = await session.LoadAsync<Product>(id);

        CacheResult(cacheKey, product, CacheKeys.Products);

        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<Product>> Create(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name))
        {
            return BadRequest("Product name is required.");
        }

        product.Id = Guid.NewGuid().ToString();

        using var session = _raven.OpenSession();

        var validationError = await ValidateProductAsync(session, product);

        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        await session.StoreAsync(product, product.Id);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Products);

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Product product)
    {
        if (id != product.Id)
        {
            return BadRequest("Route ID and body ID must match.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Product>(id);

        if (existing is null)
        {
            return NotFound();
        }

        var validationError = await ValidateProductAsync(session, product);

        if (validationError is not null)
        {
            return BadRequest(validationError);
        }

        existing.Name = product.Name;
        existing.DeliveryDays = product.DeliveryDays;
        existing.StorageLife = product.StorageLife;
        existing.UnitId = product.UnitId;
        existing.PurchaseUnitId = product.PurchaseUnitId;
        existing.PurchaseUnitRatio = product.PurchaseUnitRatio;
        existing.MinPurchaseQuantity = product.MinPurchaseQuantity;

        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Products);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Product>(id);

        if (existing is null)
        {
            return NotFound();
        }

        session.Delete(existing);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Products);

        return NoContent();
    }

    private static async Task<string?> ValidateProductAsync(
        IAsyncDocumentSession session,
        Product product)
    {
        if (string.IsNullOrWhiteSpace(product.UnitId))
        {
            product.UnitId = Unit.DefaultId;
        }

        if (string.IsNullOrWhiteSpace(product.PurchaseUnitId))
        {
            product.PurchaseUnitId = null;
        }

        if (product.PurchaseUnitRatio < 1)
        {
            return "Коэффициент единицы закупки должен быть не меньше 1.";
        }

        if (product.MinPurchaseQuantity < 0)
        {
            return "Минимальное количество закупки не может быть отрицательным.";
        }

        if (await session.LoadAsync<Unit>(product.UnitId) is null)
        {
            return $"Единица измерения '{product.UnitId}' не найдена.";
        }

        if (product.PurchaseUnitId is not null
            && await session.LoadAsync<Unit>(product.PurchaseUnitId) is null)
        {
            return $"Единица закупки '{product.PurchaseUnitId}' не найдена.";
        }

        return null;
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