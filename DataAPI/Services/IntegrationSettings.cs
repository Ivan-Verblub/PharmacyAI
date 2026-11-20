namespace DataAPI.Services;

public class IntegrationSettings
{
    public const string SectionName = "Integration";

    public AiSettings Ai { get; set; } = new();

    public AnalysisSettings Analysis { get; set; } = new();

    public ImportSettings Product { get; set; } = new();
    public ImportSettings Flow { get; set; } = new();
    public ImportSettings Stock { get; set; } = new();
}

public class AiSettings
{
    public string Provider { get; set; } = "Ollama";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gemma4:31b";
    public string BaseUrl { get; set; } = "http://localhost:11434/v1/";
    public int TimeoutSeconds { get; set; } = 60;
}

public class AnalysisSettings
{
    /// <summary>Глубина анализа истории движений по умолчанию, дней.</summary>
    public int HistoryDays { get; set; } = 90;

    /// <summary>Z-коэффициент уровня сервиса для страхового запаса (1.65 ≈ 95%).</summary>
    public double ServiceLevelZ { get; set; } = 1.65;

    /// <summary>Период закупки по умолчанию на странице анализа.</summary>
    public string DefaultPurchasePeriod { get; set; } = "Month";
}

public class ImportSettings
{
    public string FilePath { get; set; } = string.Empty;
    public string FileType { get; set; } = "Csv";
    public bool IsEnabled { get; set; }
    public ImportScheduleSettings Schedule { get; set; } = new();
}

public class ImportScheduleSettings
{
    public bool IsEnabled { get; set; }
    public List<string> DaysOfWeek { get; set; } = [];
    public TimeOnly StartTime { get; set; } = new(8, 0);
    public TimeOnly EndTime { get; set; } = new(18, 0);
    public int RepeatIntervalSeconds { get; set; } = 60;
    public string RepeatMode { get; set; } = "Interval";
    public int? EveryDays { get; set; }
    public int? DayOfMonth { get; set; }
    public bool SkipHolidays { get; set; }
    public bool RunImmediatelyAfterSave { get; set; }
    public bool StopOnError { get; set; }
    public int MaxAttempts { get; set; } = 3;
}