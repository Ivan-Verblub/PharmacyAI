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

        try
        {
            ValidateSettings(settings);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

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
            "Branch" => settings.Branch,
            "Unit" => settings.Unit,
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
        ValidateImportSettings(settings.Branch, nameof(settings.Branch));
        ValidateImportSettings(settings.Unit, nameof(settings.Unit));

        if (settings.Analysis.HistoryDays < 7 || settings.Analysis.HistoryDays > 3 * 366)
        {
            throw new ArgumentException(
                "Период анализа должен быть от 7 до 1098 дней.");
        }

        if (settings.Analysis.ServiceLevelZ <= 0)
        {
            throw new ArgumentException(
                "Коэффициент уровня сервиса должен быть больше нуля.");
        }

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