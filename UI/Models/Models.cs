namespace UI.Models;

public class Product
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DeliveryDays { get; set; }
    public int StorageLife { get; set; }
    public string UnitId { get; set; } = MeasureUnit.DefaultId;
    public string? PurchaseUnitId { get; set; }
    public int PurchaseUnitRatio { get; set; } = 1;
    public int MinPurchaseQuantity { get; set; }
}

public class MeasureUnit
{
    public const string DefaultId = "unit-pcs";

    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(ShortName) ? Name : ShortName;
}

public class Branch
{
    public const string DefaultId = "branch-main";

    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Flow
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string ProductId { get; set; } = string.Empty;
    public string BranchId { get; set; } = Branch.DefaultId;
    public int QuantityIncome { get; set; }
    public int QuantityOutcome { get; set; }
    public int ReserveIncome { get; set; }
    public int ReserveOutcome { get; set; }
}

public class Stock
{
    public string Id { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string BranchId { get; set; } = Branch.DefaultId;
    public int Quantity { get; set; }
    public int Reserve { get; set; }
    public int Transit { get; set; }
}

public class Recommendation
{
    public string Id { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string ProductId { get; set; } = string.Empty;
    public string BranchId { get; set; } = Branch.DefaultId;
    public int Quantity { get; set; }
    public int CalculatedQuantity { get; set; }
    public int MinPurchaseQuantity { get; set; }
    public int PurchaseUnitQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? AnalysisFrom { get; set; }
    public DateTime? AnalysisTo { get; set; }
    public DateTime? PurchaseFrom { get; set; }
    public DateTime? PurchaseTo { get; set; }
    public string PurchasePeriod { get; set; } = string.Empty;

    public bool MinPurchaseApplied =>
        MinPurchaseQuantity > 0 && CalculatedQuantity < MinPurchaseQuantity;
}

public class BranchRecommendationSummary
{
    public string BranchId { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int Positions { get; set; }
    public int TotalQuantity { get; set; }
    public int CalculatedQuantity { get; set; }
    public int MinAppliedPositions { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class RecommendationAnalysisStatus
{
    public bool HasRecommendations { get; set; }
    public DateTime? LastRecommendationDate { get; set; }
    public int TotalCount { get; set; }
    public DateTime? AnalysisFrom { get; set; }
    public DateTime? AnalysisTo { get; set; }
    public DateTime? PurchaseFrom { get; set; }
    public DateTime? PurchaseTo { get; set; }
    public string PurchasePeriod { get; set; } = string.Empty;
}

public class RecommendationAnalysisRequest
{
    public DateTime TargetDate { get; set; } = DateTime.UtcNow.Date;
    public string PurchasePeriod { get; set; } = "Month";
    public DateTime? PurchaseTo { get; set; }
    public int? HistoryDays { get; set; }
    public DateTime? AnalysisFrom { get; set; }
    public DateTime? AnalysisTo { get; set; }
    public List<string> BranchIds { get; set; } = [];
}

public class AnalysisSettings
{
    public int HistoryDays { get; set; } = 90;
    public double ServiceLevelZ { get; set; } = 1.65;
    public string DefaultPurchasePeriod { get; set; } = "Month";
}

public class IntegrationSettings
{
    public AiSettings Ai { get; set; } = new();
    public AnalysisSettings Analysis { get; set; } = new();
    public ImportSettings Product { get; set; } = new();
    public ImportSettings Flow { get; set; } = new();
    public ImportSettings Stock { get; set; } = new();
    public ImportSettings Branch { get; set; } = new();
    public ImportSettings Unit { get; set; } = new();
}

public class AiSettings
{
    public string Provider { get; set; } = "OpenAI";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public int TimeoutSeconds { get; set; } = 60;
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