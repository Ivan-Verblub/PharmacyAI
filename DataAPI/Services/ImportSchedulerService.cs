using DataAPI.Services;
using Microsoft.Extensions.Options;

namespace DataAPI.Services;

public class ImportSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrationSettingsStore _settingsStore;
    private readonly ILogger<ImportSchedulerService> _logger;

    public ImportSchedulerService(
        IServiceScopeFactory scopeFactory,
        IntegrationSettingsStore settingsStore,
        ILogger<ImportSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _settingsStore = settingsStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);

                var settings = await _settingsStore.LoadAsync(stoppingToken);

                await RunIfDueAsync("Product", settings.Product, stoppingToken);
                await RunIfDueAsync("Flow", settings.Flow, stoppingToken);
                await RunIfDueAsync("Stock", settings.Stock, stoppingToken);
                await RunIfDueAsync("Branch", settings.Branch, stoppingToken);
                await RunIfDueAsync("Unit", settings.Unit, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при выполнении запланированного импорта.");
            }
        }
    }

    private async Task RunIfDueAsync(
        string entityType,
        ImportSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.IsEnabled ||
            !settings.Schedule.IsEnabled ||
            string.IsNullOrWhiteSpace(settings.FilePath))
        {
            return;
        }

        var now = TimeOnly.FromDateTime(DateTime.Now);

        if (now < settings.Schedule.StartTime ||
            now > settings.Schedule.EndTime)
        {
            return;
        }

        if (settings.Schedule.RepeatMode == "Weekly" &&
            !settings.Schedule.DaysOfWeek.Contains(
                DateTime.Now.DayOfWeek.ToString()))
        {
            return;
        }

        if (settings.Schedule.RepeatMode == "Monthly" &&
            settings.Schedule.DayOfMonth is int day &&
            DateTime.Now.Day != day)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();

        var importService = scope.ServiceProvider
            .GetRequiredService<DataImportService>();

        var count = await importService.ImportAsync(
            entityType,
            settings,
            cancellationToken);

        _logger.LogInformation(
            "Запланированный импорт {EntityType} завершён. Загружено записей: {Count}.",
            entityType,
            count);
    }
}