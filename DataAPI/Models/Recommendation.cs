namespace DataAPI.Models
{
    public class Recommendation
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string ProductId { get; set; } = string.Empty;
        public string BranchId { get; set; } = Branch.DefaultId;

        /// <summary>Итоговое количество к закупке, в базовых единицах.</summary>
        public int Quantity { get; set; }

        /// <summary>Расчётная потребность до применения минимума и округления.</summary>
        public int CalculatedQuantity { get; set; }

        /// <summary>Обязательный минимум, действовавший на момент расчёта.</summary>
        public int MinPurchaseQuantity { get; set; }

        /// <summary>Количество в единицах закупки (упаковках).</summary>
        public int PurchaseUnitQuantity { get; set; }

        public string Reason { get; set; } = string.Empty;

        public DateTime? AnalysisFrom { get; set; }
        public DateTime? AnalysisTo { get; set; }
        public DateTime? PurchaseFrom { get; set; }
        public DateTime? PurchaseTo { get; set; }
        public string PurchasePeriod { get; set; } = string.Empty;
    }

    public class RecommendationAnalysisRequest
    {
        /// <summary>Дата начала периода закупки.</summary>
        public DateTime TargetDate { get; set; }

        /// <summary>Week | Month | Quarter | HalfYear | Year | Custom.</summary>
        public string PurchasePeriod { get; set; } = PurchasePeriods.Month;

        /// <summary>Дата окончания периода закупки (для Custom).</summary>
        public DateTime? PurchaseTo { get; set; }

        /// <summary>Глубина анализа истории, дней. Null — значение из настроек.</summary>
        public int? HistoryDays { get; set; }

        /// <summary>Произвольный период анализа истории (имеет приоритет над HistoryDays).</summary>
        public DateTime? AnalysisFrom { get; set; }
        public DateTime? AnalysisTo { get; set; }

        /// <summary>Филиалы для расчёта. Пусто — все активные.</summary>
        public List<string> BranchIds { get; set; } = [];
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

    public class BranchRecommendationSummary
    {
        public string BranchId { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public int Positions { get; set; }
        public int TotalQuantity { get; set; }
        public int CalculatedQuantity { get; set; }
        public int MinAppliedPositions { get; set; }
    }

    public static class PurchasePeriods
    {
        public const string Week = "Week";
        public const string Month = "Month";
        public const string Quarter = "Quarter";
        public const string HalfYear = "HalfYear";
        public const string Year = "Year";
        public const string Custom = "Custom";

        /// <summary>
        /// Возвращает дату окончания периода закупки (не включительно).
        /// Месяц и год считаются по календарю, а не как 30/365 дней.
        /// </summary>
        public static DateTime GetEnd(DateTime from, string? period, DateTime? customTo)
        {
            from = from.Date;

            var end = period switch
            {
                Week => from.AddDays(7),
                Month => from.AddMonths(1),
                Quarter => from.AddMonths(3),
                HalfYear => from.AddMonths(6),
                Year => from.AddYears(1),
                Custom when customTo.HasValue => customTo.Value.Date.AddDays(1),
                _ => from.AddMonths(1)
            };

            return end <= from ? from.AddDays(1) : end;
        }

        public static string GetTitle(string? period) => period switch
        {
            Week => "Неделя",
            Month => "Месяц",
            Quarter => "Квартал",
            HalfYear => "Полгода",
            Year => "Год",
            Custom => "Произвольный период",
            _ => "Месяц"
        };
    }
}
