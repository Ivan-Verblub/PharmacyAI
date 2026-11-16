using DataAPI.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;

namespace DataAPI.Services;

public record ProductForecastContext(
    string ProductId,
    string Name,
    DateTime TargetDate,
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

    int ForecastDemandToTargetDate,
    double ForecastDailyDemand,
    int SafetyStock,
    int ReorderPoint,
    int ProjectedStockAtTargetDate,
    bool NeedPurchase,
    int PrecalculatedQuantity,
    string Reason);

public record RecommendationPrompt(
    DateTime GeneratedAt,
    DateTime TargetDate,
    double ServiceLevelZ,
    IReadOnlyList<ProductForecastContext> Products);

public class RecommendationPreparationService
{
    private const int HistoryDays = 90;
    private const double ServiceLevelZ = 1.65;

    public async Task<RecommendationPrompt> BuildPromptAsync(
        IAsyncDocumentSession session,
        DateTime targetDate,
        CancellationToken cancellationToken = default)
    {
        var historyFrom = targetDate.AddDays(-HistoryDays);
        var today = DateTime.UtcNow.Date;

        var products = await session.Query<Product>()
            .ToListAsync(cancellationToken);

        var flows = await session.Query<Flow>()
            .Where(x => x.Date >= historyFrom && x.Date <= targetDate)
            .ToListAsync(cancellationToken);

        var stocks = await session.Query<Stock>()
            .ToListAsync(cancellationToken);

        var contexts = new List<ProductForecastContext>();

        foreach (var product in products)
        {
            var productFlows = flows
                .Where(x => x.ProductId == product.Id)
                .ToList();

            var dailyDemand = productFlows
                .GroupBy(x => x.Date.Date)
                .ToDictionary(
                    x => x.Key,
                    x => x.Sum(y => y.QuantityOutcome));

            var totalDemand = dailyDemand.Values.Sum();
            var activeDemandDays = dailyDemand.Values.Count(x => x > 0);

            var averageDailyDemand = activeDemandDays == 0
                ? 0
                : (double)totalDemand / activeDemandDays;

            var maxDailyDemand = dailyDemand.Values.Count == 0
                ? 0
                : dailyDemand.Values.Max();

            var demandStdDev = CalculateStdDev(dailyDemand.Values);
            var trendPerDay = CalculateTrend(dailyDemand);

            var averageByWeekday = dailyDemand
                .GroupBy(x => x.Key.DayOfWeek)
                .ToDictionary(
                    x => x.Key,
                    x => x.Average(y => y.Value));

            var averageByMonth = dailyDemand
                .GroupBy(x => x.Key.Month)
                .ToDictionary(
                    x => x.Key,
                    x => x.Average(y => y.Value));

            var weekdaySeasonalIndex = CalculateWeekdaySeasonalIndex(
                averageByWeekday,
                targetDate.DayOfWeek);

            var monthSeasonalIndex = CalculateMonthSeasonalIndex(
                averageByMonth,
                targetDate.Month);

            var (hasSeasonality, peakThreshold, recentPeakDays, peakUplift) =
                DetectPeaks(dailyDemand, averageDailyDemand);

            var daysAhead = Math.Max(0, (targetDate - today).Days);

            var baseForecastDailyDemand = Math.Max(
                0,
                averageDailyDemand + trendPerDay * daysAhead);

            var seasonalFactor = weekdaySeasonalIndex * monthSeasonalIndex;

            var forecastDailyDemand = baseForecastDailyDemand * seasonalFactor;

            if (hasSeasonality)
            {
                forecastDailyDemand *= Math.Max(1.0, peakUplift);
            }

            forecastDailyDemand = Math.Max(0, forecastDailyDemand);

            var forecastDemandToTargetDate =
                (int)Math.Ceiling(forecastDailyDemand * daysAhead);

            var stock = stocks
                .Where(x => x.ProductId == product.Id)
                .OrderByDescending(x => x.Id)
                .FirstOrDefault();

            var availableStock = stock is null
                ? 0
                : stock.Quantity - stock.Reserve + stock.Transit;

            var seasonalStdDev = hasSeasonality
                ? Math.Max(demandStdDev, demandStdDev * peakUplift)
                : demandStdDev;

            var safetyStock = (int)Math.Ceiling(
                ServiceLevelZ * seasonalStdDev * Math.Sqrt(product.DeliveryDays));

            var leadTimeDemand = (int)Math.Ceiling(
                forecastDailyDemand * product.DeliveryDays);

            var reorderPoint = leadTimeDemand + safetyStock;

            var projectedStock = availableStock - forecastDemandToTargetDate;
            var needPurchase = projectedStock < reorderPoint;

            var targetStock = forecastDemandToTargetDate + safetyStock;
            var recommendedQuantity = Math.Max(0, targetStock - projectedStock);

            var maxStockByStorageLife = (int)Math.Ceiling(
                forecastDailyDemand * product.StorageLife);

            recommendedQuantity = Math.Min(
                recommendedQuantity,
                maxStockByStorageLife);

            if (!needPurchase || recommendedQuantity <= 0)
            {
                continue;
            }

            contexts.Add(new ProductForecastContext(
                 product.Id,
                 product.Name,
                 targetDate,
                 product.DeliveryDays,
                 product.StorageLife,
                 availableStock,
                 stock?.Reserve ?? 0,
                 stock?.Transit ?? 0,
                 HistoryDays,
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

                 forecastDemandToTargetDate,
                 Math.Round(forecastDailyDemand, 2),
                 safetyStock,
                 reorderPoint,
                 projectedStock,
                 needPurchase,
                 recommendedQuantity,
                 ""));
        }

        return new RecommendationPrompt(
            DateTime.UtcNow,
            targetDate,
            ServiceLevelZ,
            contexts);
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

    public async Task SaveRecommendationsAsync(
    IAsyncDocumentSession session,
    DateTime targetDate,
    IReadOnlyList<ProductForecastContext> contexts,
    CancellationToken cancellationToken = default)
    {

        var existing = await session.Query<Recommendation>()
            .ToListAsync(cancellationToken);

        foreach (var recommendation in existing)
        {
            session.Delete(recommendation);
        }

        foreach (var context in contexts)
        {
            var recommendation = new Recommendation
            {
                Id = Guid.NewGuid().ToString(),
                Date = targetDate,
                ProductId = context.ProductId,
                Quantity = context.PrecalculatedQuantity,
                Reason = context.Reason
            };

            await session.StoreAsync(recommendation, recommendation.Id, cancellationToken);
        }

        await session.SaveChangesAsync(cancellationToken);
    }

    private static double CalculateWeekdaySeasonalIndex(
    IReadOnlyDictionary<DayOfWeek, double> averageByWeekday,
    DayOfWeek targetDayOfWeek)
    {
        if (averageByWeekday.Count == 0)
        {
            return 1.0;
        }

        var overallAverage = averageByWeekday.Values.Average();

        if (overallAverage <= 0)
        {
            return 1.0;
        }

        var targetAverage = averageByWeekday
            .GetValueOrDefault(targetDayOfWeek, overallAverage);

        return Math.Clamp(targetAverage / overallAverage, 0.25, 3.0);
    }

    private static double CalculateMonthSeasonalIndex(
        IReadOnlyDictionary<int, double> averageByMonth,
        int targetMonth)
    {
        if (averageByMonth.Count == 0)
        {
            return 1.0;
        }

        var overallAverage = averageByMonth.Values.Average();

        if (overallAverage <= 0)
        {
            return 1.0;
        }

        var targetAverage = averageByMonth
            .GetValueOrDefault(targetMonth, overallAverage);

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