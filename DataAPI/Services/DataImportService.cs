using DataAPI.Infrastructure;
using DataAPI.Services;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session;

namespace DataAPI.Services;

public class DataImportService
{
    private readonly IRavenDbService _raven;
    private readonly FileImportReader _reader;

    public DataImportService(
        IRavenDbService raven,
        FileImportReader reader)
    {
        _raven = raven;
        _reader = reader;
    }

    public async Task<int> ImportAsync(
        string entityType,
        ImportSettings settings,
        CancellationToken cancellationToken = default)
    {
        var collectionName = GetCollectionName(entityType);

        await ClearCollectionAsync(
            collectionName,
            cancellationToken);

        using var session = _raven.OpenSession();

        var importedCount = entityType switch
        {
            "Product" => await ImportProductsAsync(
                session,
                settings,
                cancellationToken),

            "Flow" => await ImportFlowsAsync(
                session,
                settings,
                cancellationToken),

            "Stock" => await ImportStocksAsync(
                session,
                settings,
                cancellationToken),

            _ => throw new NotSupportedException(
                $"Неизвестный тип импорта: {entityType}")
        };

        await session.SaveChangesAsync(cancellationToken);

        return importedCount;
    }

    private static string GetCollectionName(string entityType)
    {
        return entityType switch
        {
            "Product" => "Products",
            "Flow" => "Flows",
            "Stock" => "Stocks",
            _ => throw new NotSupportedException(
                $"Неизвестный тип импорта: {entityType}")
        };
    }

    private async Task ClearCollectionAsync(
        string collectionName,
        CancellationToken cancellationToken)
    {
        using var session = _raven.OpenSession();

        var operation = new DeleteByQueryOperation(
            $"from '{collectionName}'");

        var operationId = session.Advanced.DocumentStore.Operations
            .SendAsync(operation, null, cancellationToken);

        var operationResult = await operationId;

        await operationResult.WaitForCompletionAsync(cancellationToken);
    }

    private async Task<int> ImportProductsAsync(
        IAsyncDocumentSession session,
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        var items = await _reader.ReadProductsAsync(
            settings,
            cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString();
            }

            await session.StoreAsync(item, item.Id, cancellationToken);
        }

        return items.Count;
    }

    private async Task<int> ImportFlowsAsync(
        IAsyncDocumentSession session,
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        var items = await _reader.ReadFlowsAsync(
            settings,
            cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString();
            }

            await session.StoreAsync(item, item.Id, cancellationToken);
        }

        return items.Count;
    }

    private async Task<int> ImportStocksAsync(
        IAsyncDocumentSession session,
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        var items = await _reader.ReadStocksAsync(
            settings,
            cancellationToken);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString();
            }

            await session.StoreAsync(item, item.Id, cancellationToken);
        }

        return items.Count;
    }
}