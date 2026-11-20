using DataAPI.Infrastructure;
using DataAPI.Models;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;

namespace DataAPI.Services;

/// <summary>
/// Подготавливает базу к работе с филиалами и единицами измерения:
/// создаёт филиал и единицу по умолчанию и проставляет их в существующих документах.
/// Безопасно выполняется при каждом запуске.
/// </summary>
public class DataMigrationService : IHostedService
{
    private readonly IRavenDbService _raven;
    private readonly ILogger<DataMigrationService> _logger;

    public DataMigrationService(
        IRavenDbService raven,
        ILogger<DataMigrationService> logger)
    {
        _raven = raven;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureDefaultsAsync(cancellationToken);

            await PatchAsync(
                "Flows",
                $"if (!this.BranchId) {{ this.BranchId = '{Branch.DefaultId}'; }}",
                cancellationToken);

            await PatchAsync(
                "Stocks",
                $"if (!this.BranchId) {{ this.BranchId = '{Branch.DefaultId}'; }}",
                cancellationToken);

            await PatchAsync(
                "Recommendations",
                $"if (!this.BranchId) {{ this.BranchId = '{Branch.DefaultId}'; }}",
                cancellationToken);

            await PatchAsync(
                "Products",
                $"if (!this.UnitId) {{ this.UnitId = '{Unit.DefaultId}'; }} " +
                "if (!this.PurchaseUnitRatio || this.PurchaseUnitRatio < 1) { this.PurchaseUnitRatio = 1; } " +
                "if (this.MinPurchaseQuantity == null) { this.MinPurchaseQuantity = 0; }",
                cancellationToken);

            _raven.InvalidateCache(
                Common.CacheKeys.Flows,
                Common.CacheKeys.Stocks,
                Common.CacheKeys.Products,
                Common.CacheKeys.Recommendations,
                Common.CacheKeys.Branches,
                Common.CacheKeys.Units);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data migration for branches and units failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        using var session = _raven.OpenSession();

        if (await session.LoadAsync<Branch>(Branch.DefaultId, cancellationToken) is null)
        {
            await session.StoreAsync(new Branch
            {
                Id = Branch.DefaultId,
                Code = "MAIN",
                Name = "Основной",
                IsActive = true
            }, Branch.DefaultId, cancellationToken);
        }

        var defaultUnits = new[]
        {
            new Unit { Id = Unit.DefaultId, Code = "796", Name = "Штука", ShortName = "шт." },
            new Unit { Id = "unit-pack", Code = "778", Name = "Упаковка", ShortName = "уп." }
        };

        foreach (var unit in defaultUnits)
        {
            if (await session.LoadAsync<Unit>(unit.Id, cancellationToken) is null)
            {
                await session.StoreAsync(unit, unit.Id, cancellationToken);
            }
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    private async Task PatchAsync(
        string collection,
        string script,
        CancellationToken cancellationToken)
    {
        using var session = _raven.OpenSession();

        var operation = await session.Advanced.DocumentStore.Operations.SendAsync(
            new PatchByQueryOperation(new IndexQuery
            {
                Query = $"from '{collection}' update {{ {script} }}"
            }),
            token: cancellationToken);

        await operation.WaitForCompletionAsync(TimeSpan.FromMinutes(5));
    }
}
