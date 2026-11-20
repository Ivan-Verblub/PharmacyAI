using DataAPI.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;

namespace DataAPI.Services;

public record ProductForecastContext(
    string ProductId,
    string Name,
    string BranchId,
    string BranchName,
    string UnitName,
    string PurchaseUnitName,
    int PurchaseUnitRatio,
    DateTime TargetDate,
    DateTime PurchaseFrom,
    DateTime PurchaseTo,
    int HorizonDays,
    int DeliveryDays,
    int StorageLife,
    int AvailableStock,
    int Reserve,
    int Transit,
    int HistoryDays,
    int TotalDemand,
    int ActiveDemandDays,
    double AverageDailyDemand,
    int MaxDailyDemand,
    double DemandStdDev,
    double TrendPerDay,

    double WeekdaySeasonalIndex,
    double MonthSeasonalIndex,
    bool HasSeasonality,
    double PeakDemandThreshold,
    int RecentPeakDays,
    double PeakUpliftFactor,

    int ForecastDemandUntilStart,
    int ForecastDemandToTargetDate,
    double ForecastDailyDemand,
    int SafetyStock,
    int ReorderPoint,
    int ProjectedStockAtTargetDate,
    bool NeedPurchase,
    int CalculatedQuantity,
    int MinPurchaseQuantity,
    bool MinPurchaseApplied,
    int PrecalculatedQuantity,
    string Reason);

public record RecommendationPrompt(
    DateTime GeneratedAt,
    DateTime TargetDate,
    DateTime AnalysisFrom,
    DateTime AnalysisTo,
    DateTime PurchaseFrom,
    DateTime PurchaseTo,
    string PurchasePeriod,
    double ServiceLevelZ,
    IReadOnlyList<string> BranchIds,
    IReadOnlyList<ProductForecastContext> Products);

public class RecommendationPreparationService
{
    private const int MaxHistoryDays = 3 * 366;

    public async Task<RecommendationPrompt> BuildPromptAsync(
        IAsyncDocumentSession session,
        RecommendationAnalysisRequest request,
        AnalysisSettings settings,
        CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;

        // Период анализа истории: произвольный диапазон или N дней до сегодняшнего дня.
        DateTime analysisFrom;
        DateTime analysisTo;

        if (request.AnalysisFrom.HasValue && request.AnalysisTo.HasValue
            && request.AnalysisFrom.Value.Date <= request.AnalysisTo.Value.Date)
        {
            analysisFrom = request.AnalysisFrom.Value.Date;
            analysisTo = request.AnalysisTo.Value.Date;
        }
        else
        {
            var historyDays = Math.Clamp(
                request.HistoryDays ?? settings.HistoryDays,
                7,
                MaxHistoryDays);

            analysisTo = today;
            analysisFrom = today.AddDays(-historyDays + 1);
        }

        // Период закупки: календарный горизонт, на который рассчитывается потребность.
        var purchaseFrom = (request.TargetDate == default ? today : request.TargetDate).Date;

        if (purchaseFrom < today)
        {
            purchaseFrom = today;
        }

        var purchaseEnd = PurchasePeriods.GetEnd(
            purchaseFrom,
            request.PurchasePeriod,
            request.PurchaseTo);

        var horizonDays = (purchaseEnd - purchaseFrom).Days;
        var daysUntilStart = Math.Max(0, (purchaseFrom - today).Days);
        var serviceLevelZ = settings.ServiceLevelZ > 0 ? settings.ServiceLevelZ : 1.65;

        var products = await session.Query<Product>()
            .ToListAsync(cancellationToken);

        var units = (await session.Query<Unit>().ToListAsync(cancellationToken))
            .ToDictionary(x => x.Id);

        var allBranches = await session.Query<Branch>()
            .ToListAsync(cancellationToken);

        var branches = request.BranchIds.Count > 0
            ? allBranches.Where(x => request.BranchIds.Contains(x.Id)).ToList()
            : allBranches.Where(x => x.IsActive).ToList();

        if (branches.Count == 0 && request.BranchIds.Count == 0)
        {
            branches.Add(new Branch { Id = Branch.DefaultId, Name = "Основной" });
        }

        var branchIds = branches.Select(x => x.Id).ToList();
        var analysisToExclusive = analysisTo.AddDays(1);

        var flows = await session.Query<Flow>()
            .Where(x => x.Date >= analysisFrom && x.Date < analysisToExclusive)
            .ToListAsync(cancellationToken);

        var stocks = await session.Query<Stock>()
            .ToListAsync(cancellationToken);

        var flowsByKey = flows
            .Where(x => branchIds.Contains(NormalizeBranch(x.BranchId)))
            .GroupBy(x => (x.ProductId, BranchId: NormalizeBranch(x.BranchId)))
            .ToDictionary(x => x.Key, x => x.ToList());

        var stocksByKey = stocks
            .GroupBy(x => (x.ProductId, BranchId: NormalizeBranch(x.BranchId)))
            .ToDictionary(x => x.Key, x => x.ToList());

        var contexts = new List<ProductForecastContext>();

        foreach (var branch in branches)
        {
            foreach (var product in products)
            {
                var context = BuildContext(
                    product,
                    branch,
                    units,
                    flowsByKey.GetValueOrDefault((product.Id, branch.Id)) ?? [],
                    stocksByKey.GetValueOrDefault((product.Id, branch.Id)) ?? [],
                    today,
                    analysisFrom,
                    analysisTo,
                    purchaseFrom,
                    purchaseEnd,
                    horizonDays,
                    daysUntilStart,
                    serviceLevelZ);

                if (context is not null)
                {
                    contexts.Add(context);
                }
            }
        }

        return new RecommendationPrompt(
            DateTime.UtcNow,
            purchaseFrom,
            analysisFrom,
            analysisTo,
            purchaseFrom,
            purchaseEnd.AddDays(-1),
            string.IsNullOrWhiteSpace(request.PurchasePeriod)
                ? PurchasePeriods.Month
                : request.PurchasePeriod,
            serviceLevelZ,
            branchIds,
            contexts);
    }

    private static ProductForecastContext? BuildContext(
        Product product,
        Branch branch,
        IReadOnlyDictionary<string, Unit> units,
        IReadOnlyList<Flow> productFlows,
        IReadOnlyList<Stock> productStocks,
        DateTime today,
        DateTime analysisFrom,
        DateTime analysisTo,
        DateTime purchaseFrom,
        DateTime purchaseEnd,
        int horizonDays,
        int daysUntilStart,
        double serviceLevelZ)
    {
        // Дневной спрос с нулями в днях без продаж: иначе среднее и разброс завышаются.
        var effectiveFrom = productFlows.Count == 0
            ? analysisFrom
            : Max(analysisFrom, productFlows.Min(x => x.Date.Date));

        var dailyDemand = new SortedDictionary<DateTime, int>();

        for (var day = effectiveFrom; day <= analysisTo; day = day.AddDays(1))
        {
            dailyDemand[day] = 0;
        }

        foreach (var flow in productFlows)
        {
            var day = flow.Date.Date;

            if (dailyDemand.ContainsKey(day))
            {
                dailyDemand[day] += Math.Max(0, flow.QuantityOutcome);
            }
        }

        var historyDays = dailyDemand.Count;
        var totalDemand = dailyDemand.Values.Sum();
        var activeDemandDays = dailyDemand.Values.Count(x => x > 0);

        var averageDailyDemand = historyDays == 0
            ? 0
            : (double)totalDemand / historyDays;

        var maxDailyDemand = dailyDemand.Count == 0 ? 0 : dailyDemand.Values.Max();
        var demandStdDev = CalculateStdDev(dailyDemand.Values);
        var trendPerDay = CalculateTrend(dailyDemand);

        var averageByWeekday = dailyDemand
            .GroupBy(x => x.Key.DayOfWeek)
            .ToDictionary(x => x.Key, x => x.Average(y => y.Value));

        var averageByMonth = dailyDemand
            .GroupBy(x => x.Key.Month)
            .ToDictionary(x => x.Key, x => x.Average(y => y.Value));

        var (hasSeasonality, peakThreshold, recentPeakDays, peakUplift) =
            DetectPeaks(dailyDemand, averageDailyDemand);

        // Прогноз по дням: тренд (с ограничением), сезонность по дню недели и месяцу.
        // Пиковый коэффициент применяется только к коротким горизонтам, на длинных
        // периодах пики уже отражены в среднем спросе.
        var applyPeakUplift = hasSeasonality && horizonDays <= 14;
        var maxTrendShift = averageDailyDemand;

        double ForecastForDay(DateTime day)
        {
            var daysAhead = (day - today).Days;
            var trendShift = Math.Clamp(trendPerDay * daysAhead, -maxTrendShift, maxTrendShift);
            var baseDemand = Math.Max(0, averageDailyDemand + trendShift);

            var factor =
                CalculateSeasonalIndex(averageByWeekday, day.DayOfWeek) *
                CalculateSeasonalIndex(averageByMonth, day.Month);

            if (applyPeakUplift)
            {
                factor *= Math.Max(1.0, peakUplift);
            }

            return baseDemand * factor;
        }

        double demandUntilStartRaw = 0;

        for (var day = today; day < purchaseFrom; day = day.AddDays(1))
        {
            demandUntilStartRaw += ForecastForDay(day);
        }

        double demandHorizonRaw = 0;

        for (var day = purchaseFrom; day < purchaseEnd; day = day.AddDays(1))
        {
            demandHorizonRaw += ForecastForDay(day);
        }

        var forecastDemandUntilStart = (int)Math.Ceiling(demandUntilStartRaw);
        var forecastDemandOnHorizon = (int)Math.Ceiling(demandHorizonRaw);
        var forecastDailyDemand = horizonDays == 0 ? 0 : demandHorizonRaw / horizonDays;

        var weekdaySeasonalIndex = AverageIndex(
            purchaseFrom, purchaseEnd, d => CalculateSeasonalIndex(averageByWeekday, d.DayOfWeek));

        var monthSeasonalIndex = AverageIndex(
            purchaseFrom, purchaseEnd, d => CalculateSeasonalIndex(averageByMonth, d.Month));

        var quantity = productStocks.Sum(x => x.Quantity);
        var reserve = productStocks.Sum(x => x.Reserve);
        var transit = productStocks.Sum(x => x.Transit);
        var availableStock = quantity - reserve + transit;

        var seasonalStdDev = hasSeasonality
            ? Math.Max(demandStdDev, demandStdDev * peakUplift)
            : demandStdDev;

        var safetyStock = (int)Math.Ceiling(
            serviceLevelZ * seasonalStdDev * Math.Sqrt(Math.Max(1, product.DeliveryDays)));

        var leadTimeDemand = (int)Math.Ceiling(forecastDailyDemand * product.DeliveryDays);
        var reorderPoint = leadTimeDemand + safetyStock;

        // Остаток к началу периода закупки. Отрицательный остаток означает дефицит
        // до начала периода — упущенный спрос не восполняется, поэтому берём не ниже нуля.
        var projectedStock = availableStock - forecastDemandUntilStart;
        var stockAtStart = Math.Max(0, projectedStock);

        var targetStock = forecastDemandOnHorizon + safetyStock;
        var calculatedQuantity = Math.Max(0, targetStock - stockAtStart);

        // Ограничение по сроку годности: не закупать больше, чем успеем продать.
        if (product.StorageLife > 0 && forecastDailyDemand > 0)
        {
            var maxStockByStorageLife = (int)Math.Ceiling(
                forecastDailyDemand * product.StorageLife);

            calculatedQuantity = Math.Min(
                calculatedQuantity,
                Math.Max(0, maxStockByStorageLife - stockAtStart));
        }

        var needPurchase = calculatedQuantity > 0;

        // Безусловный минимум закупки.
        var minPurchase = Math.Max(0, product.MinPurchaseQuantity);
        var minApplied = minPurchase > calculatedQuantity;
        var finalQuantity = Math.Max(calculatedQuantity, minPurchase);

        var ratio = Math.Max(1, product.PurchaseUnitRatio);
        finalQuantity = RoundUpToRatio(finalQuantity, ratio);

        if (finalQuantity <= 0)
        {
            return null;
        }

        var unitName = GetUnitName(units, product.UnitId, "шт.");
        var purchaseUnitName = string.IsNullOrWhiteSpace(product.PurchaseUnitId)
            ? unitName
            : GetUnitName(units, product.PurchaseUnitId, unitName);

        return new ProductForecastContext(
            product.Id,
            product.Name,
            branch.Id,
            string.IsNullOrWhiteSpace(branch.Name) ? branch.Id : branch.Name,
            unitName,
            purchaseUnitName,
            ratio,
            purchaseFrom,
            purchaseFrom,
            purchaseEnd.AddDays(-1),
            horizonDays,
            product.DeliveryDays,
            product.StorageLife,
            availableStock,
            reserve,
            transit,
            historyDays,
            totalDemand,
            activeDemandDays,
            Math.Round(averageDailyDemand, 2),
            maxDailyDemand,
            Math.Round(demandStdDev, 2),
            Math.Round(trendPerDay, 4),

            Math.Round(weekdaySeasonalIndex, 2),
            Math.Round(monthSeasonalIndex, 2),
            hasSeasonality,
            Math.Round(peakThreshold, 2),
            recentPeakDays,
            Math.Round(peakUplift, 2),

            forecastDemandUntilStart,
            forecastDemandOnHorizon,
            Math.Round(forecastDailyDemand, 2),
            safetyStock,
            reorderPoint,
            projectedStock,
            needPurchase,
            calculatedQuantity,
            minPurchase,
            minApplied,
            finalQuantity,
            string.Empty);
    }

    public static int RoundUpToRatio(int quantity, int ratio)
    {
        if (quantity <= 0 || ratio <= 1)
        {
            return Math.Max(0, quantity);
        }

        return (int)Math.Ceiling(quantity / (double)ratio) * ratio;
    }

    /// <summary>
    /// Детерминированное пояснение на русском — используется, если ИИ-модель
    /// недоступна или не вернула позицию.
    /// </summary>
    public static string BuildFallbackReason(ProductForecastContext context)
    {
        var unit = context.UnitName;
        var parts = new List<string>();

        if (context.CalculatedQuantity > 0)
        {
            parts.Add(
                $"Прогноз спроса на период {context.PurchaseFrom:dd.MM.yyyy}–{context.PurchaseTo:dd.MM.yyyy} " +
                $"— {context.ForecastDemandToTargetDate} {unit}, страховой запас {context.SafetyStock} {unit}, " +
                $"ожидаемый остаток к началу периода {Math.Max(0, context.ProjectedStockAtTargetDate)} {unit}.");
        }
        else
        {
            parts.Add("Расчётной потребности на выбранный период нет.");
        }

        if (context.MinPurchaseApplied)
        {
            parts.Add($"Применён обязательный минимум закупки {context.MinPurchaseQuantity} {unit}.");
        }

        if (context.PurchaseUnitRatio > 1)
        {
            parts.Add(
                $"Количество округлено до {context.PrecalculatedQuantity / context.PurchaseUnitRatio} " +
                $"{context.PurchaseUnitName} по {context.PurchaseUnitRatio} {unit}.");
        }

        return string.Join(" ", parts).Replace("..", ".");
    }

    public async Task SaveRecommendationsAsync(
        IAsyncDocumentSession session,
        RecommendationPrompt prompt,
        IReadOnlyList<ProductForecastContext> contexts,
        CancellationToken cancellationToken = default)
    {
        // Удаляем только рекомендации пересчитываемых филиалов.
        var existing = await session.Query<Recommendation>()
            .ToListAsync(cancellationToken);

        foreach (var recommendation in existing
            .Where(x => prompt.BranchIds.Contains(NormalizeBranch(x.BranchId))))
        {
            session.Delete(recommendation);
        }

        foreach (var context in contexts)
        {
            var ratio = Math.Max(1, context.PurchaseUnitRatio);

            var recommendation = new Recommendation
            {
                Id = Guid.NewGuid().ToString(),
                Date = prompt.TargetDate,
                ProductId = context.ProductId,
                BranchId = context.BranchId,
                Quantity = context.PrecalculatedQuantity,
                CalculatedQuantity = context.CalculatedQuantity,
                MinPurchaseQuantity = context.MinPurchaseQuantity,
                PurchaseUnitQuantity = context.PrecalculatedQuantity / ratio,
                Reason = context.Reason,
                AnalysisFrom = prompt.AnalysisFrom,
                AnalysisTo = prompt.AnalysisTo,
                PurchaseFrom = prompt.PurchaseFrom,
                PurchaseTo = prompt.PurchaseTo,
                PurchasePeriod = prompt.PurchasePeriod
            };

            await session.StoreAsync(recommendation, recommendation.Id, cancellationToken);
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeBranch(string? branchId) =>
        string.IsNullOrWhiteSpace(branchId) ? Branch.DefaultId : branchId;

    private static string GetUnitName(
        IReadOnlyDictionary<string, Unit> units,
        string? unitId,
        string fallback)
    {
        if (unitId is not null && units.TryGetValue(unitId, out var unit))
        {
            return string.IsNullOrWhiteSpace(unit.ShortName) ? unit.Name : unit.ShortName;
        }

        return fallback;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static double AverageIndex(DateTime from, DateTime to, Func<DateTime, double> index)
    {
        double sum = 0;
        var count = 0;

        for (var day = from; day < to; day = day.AddDays(1))
        {
            sum += index(day);
            count++;
        }

        return count == 0 ? 1.0 : sum / count;
    }

    private static double CalculateStdDev(IEnumerable<int> values)
    {
        var list = values.ToList();

        if (list.Count < 2)
        {
            return 0;
        }

        var average = list.Average();
        var variance = list.Sum(x => Math.Pow(x - average, 2)) / (list.Count - 1);

        return Math.Sqrt(variance);
    }

    private static double CalculateTrend(
        IReadOnlyDictionary<DateTime, int> dailyDemand)
    {
        if (dailyDemand.Count < 2)
        {
            return 0;
        }

        var points = dailyDemand
            .OrderBy(x => x.Key)
            .Select((x, i) => (X: (double)i, Y: (double)x.Value))
            .ToList();

        var averageX = points.Average(x => x.X);
        var averageY = points.Average(x => x.Y);

        var numerator = points.Sum(x => (x.X - averageX) * (x.Y - averageY));
        var denominator = points.Sum(x => Math.Pow(x.X - averageX, 2));

        return denominator == 0 ? 0 : numerator / denominator;
    }

    private static double CalculateSeasonalIndex<TKey>(
        IReadOnlyDictionary<TKey, double> averageByKey,
        TKey key)
        where TKey : notnull
    {
        if (averageByKey.Count == 0)
        {
            return 1.0;
        }

        var overallAverage = averageByKey.Values.Average();

        if (overallAverage <= 0)
        {
            return 1.0;
        }

        var targetAverage = averageByKey.GetValueOrDefault(key, overallAverage);

        return Math.Clamp(targetAverage / overallAverage, 0.25, 3.0);
    }

    private static (bool HasSeasonality, double PeakThreshold, int PeakDays, double PeakUplift)
        DetectPeaks(
            IReadOnlyDictionary<DateTime, int> dailyDemand,
            double averageDailyDemand)
    {
        if (averageDailyDemand <= 0 || dailyDemand.Count < 14)
        {
            return (false, averageDailyDemand, 0, 1.0);
        }

        var threshold = averageDailyDemand * 1.5;

        var peakDays = dailyDemand
            .Where(x => x.Value >= threshold)
            .ToList();

        if (peakDays.Count == 0)
        {
            return (false, threshold, 0, 1.0);
        }

        var peakAverage = peakDays.Average(x => x.Value);
        var peakUplift = Math.Clamp(peakAverage / averageDailyDemand, 1.0, 3.0);

        var hasSeasonality = peakDays.Count >= 3;

        return (hasSeasonality, threshold, peakDays.Count, peakUplift);
    }
}
