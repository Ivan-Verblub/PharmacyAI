using DataAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataAPI.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly IntegrationSettingsStore _settingsStore;
    private readonly DataImportService _importService;

    public SettingsController(
        IntegrationSettingsStore settingsStore,
        DataImportService importService)
    {
        _settingsStore = settingsStore;
        _importService = importService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        return Ok(settings);
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] IntegrationSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            return BadRequest("Настройки не переданы.");
        }

        ValidateSettings(settings);

        await _settingsStore.SaveAsync(settings, cancellationToken);

        return NoContent();
    }

    [HttpPost("import/{entityType}")]
    public async Task<IActionResult> ImportNow(
        string entityType,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        var importSettings = entityType switch
        {
            "Product" => settings.Product,
            "Flow" => settings.Flow,
            "Stock" => settings.Stock,
            _ => throw new NotSupportedException(
                $"Неизвестный тип импорта: {entityType}")
        };

        var count = await _importService.ImportAsync(
            entityType,
            importSettings,
            cancellationToken);

        return Ok(new
        {
            entityType,
            importedCount = count
        });
    }

    private static void ValidateSettings(
        IntegrationSettings settings)
    {
        ValidateImportSettings(settings.Product, nameof(settings.Product));
        ValidateImportSettings(settings.Flow, nameof(settings.Flow));
        ValidateImportSettings(settings.Stock, nameof(settings.Stock));

        if (settings.Ai.TimeoutSeconds <= 0)
        {
            throw new ArgumentException(
                "Таймаут ИИ-модели должен быть больше нуля.");
        }
    }

    private static void ValidateImportSettings(
        ImportSettings settings,
        string name)
    {
        if (settings.Schedule.RepeatIntervalSeconds <= 0)
        {
            throw new ArgumentException(
                $"Интервал повтора для {name} должен быть больше нуля.");
        }

        if (settings.Schedule.StartTime > settings.Schedule.EndTime)
        {
            throw new ArgumentException(
                $"Время начала для {name} не может быть позже времени окончания.");
        }

        if (settings.Schedule.MaxAttempts <= 0)
        {
            throw new ArgumentException(
                $"Количество попыток для {name} должно быть больше нуля.");
        }
    }
}