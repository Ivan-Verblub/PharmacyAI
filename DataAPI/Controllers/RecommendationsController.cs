using DataAPI.Common;
using DataAPI.Infrastructure;
using DataAPI.Models;
using DataAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using System;

namespace DataAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecommendationsController : ControllerBase
{
    private readonly IRavenDbService _raven;
    private readonly RecommendationPreparationService _recommendationPreparationService;
    private readonly RecommendationAnalysisService _recommendationAnalysisService;
    private readonly IntegrationSettingsStore _settingsStore;

    public RecommendationsController(
        IRavenDbService raven,
        RecommendationPreparationService recommendationPreparationService,
        RecommendationAnalysisService recommendationAnalysisService,
        IntegrationSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _raven = raven;
        _recommendationPreparationService = recommendationPreparationService;
        _recommendationAnalysisService = recommendationAnalysisService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<Recommendation>>> GetAll()
    {
        var cacheKey = CacheKeys.All(CacheKeys.Recommendations);

        if (_raven.Cache.TryGetValue(cacheKey, out IReadOnlyList<Recommendation>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var items = await session.Query<Recommendation>()
            .OrderByDescending(x => x.Date)
            .ToListAsync();

        CacheResult(cacheKey, items, CacheKeys.Recommendations);

        return Ok(items);
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<Recommendation>>> GetPaged(
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
            CacheKeys.Recommendations,
            page,
            pageSize,
            $"{productId}|branch={branchId}",
            sortBy,
            sortDescending);

        if (_raven.Cache.TryGetValue(cacheKey, out PagedResult<Recommendation>? cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var query = session.Query<Recommendation>();

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

        var result = new PagedResult<Recommendation>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        CacheResult(cacheKey, result, CacheKeys.Recommendations);

        return Ok(result);
    }

    private static IRavenQueryable<Recommendation> ApplySorting(
        IRavenQueryable<Recommendation> query,
        string? sortBy,
        bool sortDescending)
    {
        return sortBy switch
        {
            nameof(Recommendation.Date) => sortDescending
                ? query.OrderByDescending(x => x.Date)
                : query.OrderBy(x => x.Date),

            nameof(Recommendation.BranchId) => sortDescending
                ? query.OrderByDescending(x => x.BranchId)
                : query.OrderBy(x => x.BranchId),

            nameof(Recommendation.CalculatedQuantity) => sortDescending
                ? query.OrderByDescending(x => x.CalculatedQuantity)
                : query.OrderBy(x => x.CalculatedQuantity),

            nameof(Recommendation.ProductId) => sortDescending
                ? query.OrderByDescending(x => x.ProductId)
                : query.OrderBy(x => x.ProductId),

            nameof(Recommendation.Quantity) => sortDescending
                ? query.OrderByDescending(x => x.Quantity)
                : query.OrderBy(x => x.Quantity),

            _ => sortDescending
                ? query.OrderByDescending(x => x.Date)
                : query.OrderBy(x => x.Date)
        };
    }

    [HttpGet("count")]
    public async Task<ActionResult<int>> GetCount()
    {
        var cacheKey = CacheKeys.Count(CacheKeys.Recommendations);

        if (_raven.Cache.TryGetValue(cacheKey, out int cached))
        {
            return Ok(cached);
        }

        using var session = _raven.OpenSession();

        var count = await session.Query<Recommendation>().CountAsync();

        CacheResult(cacheKey, count, CacheKeys.Recommendations);

        return Ok(count);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Recommendation>> GetById(string id)
    {
        var cacheKey = CacheKeys.ById(CacheKeys.Recommendations, id);

        if (_raven.Cache.TryGetValue(cacheKey, out Recommendation? cached))
        {
            return cached is null ? NotFound() : Ok(cached);
        }

        using var session = _raven.OpenSession();

        var recommendation = await session.LoadAsync<Recommendation>(id);

        CacheResult(cacheKey, recommendation, CacheKeys.Recommendations);

        return recommendation is null ? NotFound() : Ok(recommendation);
    }

    [HttpPost]
    public async Task<ActionResult<Recommendation>> Create(Recommendation recommendation)
    {
        using var session = _raven.OpenSession();

        var product = await session.LoadAsync<Product>(recommendation.ProductId);

        if (product is null)
        {
            return BadRequest($"Product '{recommendation.ProductId}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(recommendation.BranchId))
        {
            recommendation.BranchId = Branch.DefaultId;
        }

        if (await session.LoadAsync<Branch>(recommendation.BranchId) is null)
        {
            return BadRequest($"Branch '{recommendation.BranchId}' does not exist.");
        }

        recommendation.Id = Guid.NewGuid().ToString();

        await session.StoreAsync(recommendation, recommendation.Id);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Recommendations);

        return CreatedAtAction(nameof(GetById), new { id = recommendation.Id }, recommendation);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, Recommendation recommendation)
    {
        if (id != recommendation.Id)
        {
            return BadRequest("Route ID and body ID must match.");
        }

        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Recommendation>(id);

        if (existing is null)
        {
            return NotFound();
        }

        var product = await session.LoadAsync<Product>(recommendation.ProductId);

        if (product is null)
        {
            return BadRequest($"Product '{recommendation.ProductId}' does not exist.");
        }

        if (string.IsNullOrWhiteSpace(recommendation.BranchId))
        {
            recommendation.BranchId = Branch.DefaultId;
        }

        if (await session.LoadAsync<Branch>(recommendation.BranchId) is null)
        {
            return BadRequest($"Branch '{recommendation.BranchId}' does not exist.");
        }

        existing.Date = recommendation.Date;
        existing.ProductId = recommendation.ProductId;
        existing.BranchId = recommendation.BranchId;
        existing.Quantity = recommendation.Quantity;
        existing.Reason = recommendation.Reason;

        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Recommendations);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        using var session = _raven.OpenSession();

        var existing = await session.LoadAsync<Recommendation>(id);

        if (existing is null)
        {
            return NotFound();
        }

        session.Delete(existing);
        await session.SaveChangesAsync();

        _raven.InvalidateCache(CacheKeys.Recommendations);

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

    [HttpGet("analysis/status")]
    public async Task<ActionResult<RecommendationAnalysisStatus>> GetAnalysisStatus()
    {
        using var session = _raven.OpenSession();

        var lastRecommendation = await session.Query<Recommendation>()
            .OrderByDescending(x => x.Date)
            .FirstOrDefaultAsync();

        var count = await session.Query<Recommendation>()
            .CountAsync();

        return Ok(new RecommendationAnalysisStatus
        {
            HasRecommendations = count > 0,
            LastRecommendationDate = lastRecommendation?.Date,
            TotalCount = count,
            AnalysisFrom = lastRecommendation?.AnalysisFrom,
            AnalysisTo = lastRecommendation?.AnalysisTo,
            PurchaseFrom = lastRecommendation?.PurchaseFrom,
            PurchaseTo = lastRecommendation?.PurchaseTo,
            PurchasePeriod = lastRecommendation?.PurchasePeriod ?? string.Empty
        });
    }

    /// <summary>
    /// Сводка рекомендаций в разрезе филиалов.
    /// </summary>
    [HttpGet("analysis/branches")]
    public async Task<ActionResult<IReadOnlyList<BranchRecommendationSummary>>> GetBranchSummary(
        CancellationToken cancellationToken)
    {
        using var session = _raven.OpenSession();

        var recommendations = await session.Query<Recommendation>()
            .ToListAsync(cancellationToken);

        var branches = (await session.Query<Branch>().ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id);

        var summary = recommendations
            .GroupBy(x => string.IsNullOrWhiteSpace(x.BranchId) ? Branch.DefaultId : x.BranchId)
            .Select(g => new BranchRecommendationSummary
            {
                BranchId = g.Key,
                BranchName = branches.TryGetValue(g.Key, out var branch) ? branch.Name : g.Key,
                Positions = g.Count(),
                TotalQuantity = g.Sum(x => x.Quantity),
                CalculatedQuantity = g.Sum(x => x.CalculatedQuantity),
                MinAppliedPositions = g.Count(x =>
                    x.MinPurchaseQuantity > 0 && x.CalculatedQuantity < x.MinPurchaseQuantity)
            })
            .OrderBy(x => x.BranchName)
            .ToList();

        return Ok(summary);
    }

    [HttpPost("analysis")]
    public async Task<IActionResult> Analyze(
    [FromBody] RecommendationAnalysisRequest request,
    CancellationToken cancellationToken)
    {
        if (request.PurchasePeriod == PurchasePeriods.Custom
            && request.PurchaseTo.HasValue
            && request.PurchaseTo.Value.Date < request.TargetDate.Date)
        {
            return BadRequest("Дата окончания периода закупки раньше даты начала.");
        }

        if (request.AnalysisFrom.HasValue && request.AnalysisTo.HasValue
            && request.AnalysisFrom.Value.Date > request.AnalysisTo.Value.Date)
        {
            return BadRequest("Дата начала периода анализа позже даты окончания.");
        }

        var settings = await _settingsStore.LoadAsync(cancellationToken);

        using var session = _raven.OpenSession();

        var prompt = await _recommendationPreparationService
            .BuildPromptAsync(session, request, settings.Analysis, cancellationToken);

        var progress = new Progress<int>();

        var result = await _recommendationAnalysisService
            .AnalyzeAsync(prompt, progress, cancellationToken);

        await _recommendationPreparationService.SaveRecommendationsAsync(
            session,
            prompt,
            result,
            cancellationToken);

        _raven.InvalidateCache(CacheKeys.Recommendations);

        return Ok(new
        {
            prompt.AnalysisFrom,
            prompt.AnalysisTo,
            prompt.PurchaseFrom,
            prompt.PurchaseTo,
            prompt.PurchasePeriod,
            Count = result.Count
        });
    }
}