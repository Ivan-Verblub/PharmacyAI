using DataAPI.Models;
using System.Globalization;
using System.Text.Json;
using System.Xml.Serialization;

namespace DataAPI.Services;

public class FileImportReader
{
    public async Task<List<Product>> ReadProductsAsync(
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(settings, cancellationToken);

        return rows.Select(x => new Product
        {
            Id = GetValue(x, "Id"),
            Name = GetValue(x, "Name"),
            DeliveryDays = GetInt(x, "DeliveryDays"),
            StorageLife = GetInt(x, "StorageLife")
        }).ToList();
    }

    public async Task<List<Flow>> ReadFlowsAsync(
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(settings, cancellationToken);

        return rows.Select(x => new Flow
        {
            Id = GetValue(x, "Id"),
            Date = GetDate(x, "Date"),
            ProductId = GetValue(x, "ProductId"),
            QuantityIncome = GetInt(x, "QuantityIncome"),
            QuantityOutcome = GetInt(x, "QuantityOutcome"),
            ReserveIncome = GetInt(x, "ReserveIncome"),
            ReserveOutcome = GetInt(x, "ReserveOutcome")
        }).ToList();
    }

    public async Task<List<Stock>> ReadStocksAsync(
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        var rows = await ReadRowsAsync(settings, cancellationToken);

        return rows.Select(x => new Stock
        {
            Id = GetValue(x, "Id"),
            ProductId = GetValue(x, "ProductId"),
            Quantity = GetInt(x, "Quantity"),
            Reserve = GetInt(x, "Reserve"),
            Transit = GetInt(x, "Transit")
        }).ToList();
    }

    private static async Task<List<Dictionary<string, string>>> ReadRowsAsync(
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.FilePath))
        {
            throw new InvalidOperationException("Не указан путь к файлу импорта.");
        }

        if (!File.Exists(settings.FilePath))
        {
            throw new FileNotFoundException(
                "Файл импорта не найден.",
                settings.FilePath);
        }

        await using var stream = File.OpenRead(settings.FilePath);

        return settings.FileType.ToLowerInvariant() switch
        {
            "csv" => await ReadCsvAsync(stream, cancellationToken),
            "json" => await ReadJsonAsync(stream, cancellationToken),
            "xml" => await ReadXmlAsync(stream, cancellationToken),
            _ => throw new NotSupportedException(
                $"Тип файла '{settings.FileType}' не поддерживается.")
        };
    }

    private static async Task<List<Dictionary<string, string>>> ReadCsvAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var rows = new List<Dictionary<string, string>>();

        using var reader = new StreamReader(stream);

        var headerLine = await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return rows;
        }

        var headers = headerLine.Split(',');

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var values = line.Split(',');

            var row = new Dictionary<string, string>();

            for (var i = 0; i < headers.Length; i++)
            {
                row[headers[i].Trim()] =
                    i < values.Length ? values[i].Trim() : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static async Task<List<Dictionary<string, string>>> ReadJsonAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var result = await JsonSerializer.DeserializeAsync<List<Dictionary<string, string>>>(
            stream,
            cancellationToken: cancellationToken);

        return result ?? [];
    }

    private static async Task<List<Dictionary<string, string>>> ReadXmlAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var serializer = new XmlSerializer(typeof(XmlImportFile));

        var file = (XmlImportFile?)serializer.Deserialize(stream);

        return file?.Rows ?? [];
    }

    private static string GetValue(
        Dictionary<string, string> row,
        string key)
    {
        return row.TryGetValue(key, out var value)
            ? value
            : string.Empty;
    }

    private static int GetInt(
        Dictionary<string, string> row,
        string key)
    {
        return int.TryParse(GetValue(row, key), out var value)
            ? value
            : 0;
    }

    private static DateTime GetDate(
        Dictionary<string, string> row,
        string key)
    {
        return DateTime.TryParse(
            GetValue(row, key),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : DateTime.UtcNow;
    }
}

public class XmlImportFile
{
    [XmlArray("Rows")]
    [XmlArrayItem("Row")]
    public List<Dictionary<string, string>> Rows { get; set; } = [];
}