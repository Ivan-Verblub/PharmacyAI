using DataAPI.Services;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DataAPI.Services;

public class IntegrationSettingsStore
{
    private readonly string _filePath;
    private readonly IntegrationSettings _defaultSettings;

    public IntegrationSettingsStore(
        IOptionsMonitor<IntegrationSettings> options,
        IConfiguration configuration)
    {
        _defaultSettings = options.CurrentValue;

        _filePath = configuration["Integration:SettingsFilePath"]
            ?? Path.Combine(
                AppContext.BaseDirectory,
                "integration-settings.json");
    }

    public async Task<IntegrationSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return _defaultSettings;
        }

        await using var stream = File.OpenRead(_filePath);

        var settings = await JsonSerializer.DeserializeAsync<IntegrationSettings>(
            stream,
            cancellationToken: cancellationToken);

        return settings ?? _defaultSettings;
    }

    public async Task SaveAsync(
        IntegrationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_filePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);

        await JsonSerializer.SerializeAsync(
            stream,
            settings,
            new JsonSerializerOptions
            {
                WriteIndented = true
            },
            cancellationToken);
    }
}