using DataAPI.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DataAPI.Services;

public record RecommendationAnalysisResult(
    IReadOnlyList<RecommendationAnalysisItem> Recommendations);

public record RecommendationAnalysisItem(
    string ProductId,
    string? BranchId,
    DateTime TargetDate,
    int Quantity,
    string Reason);

public class RecommendationAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RecommendationAnalysisService> _logger;

    public RecommendationAnalysisService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<RecommendationAnalysisService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }

    private const int BatchSize = 20;
    private static readonly Uri ChatCompletionsUri =
        new("http://localhost:11434/v1/chat/completions");

    /// <summary>
    /// Отправляет позиции в модель пакетами. Позиции, которые модель не вернула
    /// или не смогла обработать, сохраняются с расчётным количеством и
    /// детерминированным пояснением — обязательный минимум не теряется.
    /// </summary>
    public async Task<IReadOnlyList<ProductForecastContext>> AnalyzeAsync(
        RecommendationPrompt prompt,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ProductForecastContext>();

        if (prompt.Products.Count == 0)
        {
            progress?.Report(100);
            return result;
        }

        var batches = prompt.Products.Chunk(BatchSize).ToList();

        for (var i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            IReadOnlyList<RecommendationAnalysisItem> items = [];

            try
            {
                items = await AnalyzeBatchAsync(prompt, batch, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                                       || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "AI analysis failed for batch {Batch}. Using precalculated recommendations.",
                    i + 1);
            }

            result.AddRange(MapToContexts(batch, items));

            progress?.Report(Math.Min(95, 10 + (i + 1) * 85 / batches.Count));
        }

        progress?.Report(100);

        return result;
    }

    private async Task<IReadOnlyList<RecommendationAnalysisItem>> AnalyzeBatchAsync(
        RecommendationPrompt prompt,
        IReadOnlyList<ProductForecastContext> products,
        CancellationToken cancellationToken)
    {
        var apiKey = "ollama";

        var requestData = new
        {
            model = "bonsai-2",
            stream = true,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt()
                },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(new
                    {
                        targetDate = prompt.TargetDate.ToString("yyyy-MM-dd"),
                        purchaseFrom = prompt.PurchaseFrom.ToString("yyyy-MM-dd"),
                        purchaseTo = prompt.PurchaseTo.ToString("yyyy-MM-dd"),
                        analysisFrom = prompt.AnalysisFrom.ToString("yyyy-MM-dd"),
                        analysisTo = prompt.AnalysisTo.ToString("yyyy-MM-dd"),
                        serviceLevelZ = prompt.ServiceLevelZ,
                        products
                    })
                }
            },
            response_format = new
            {
                type = "json_object"
            },
            max_completion_tokens = 4000
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            ChatCompletionsUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        request.Content = JsonContent.Create(requestData);

        using var timeoutCts = new CancellationTokenSource(
            TimeSpan.FromSeconds(120));

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            cancellationToken);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            linkedCts.Token);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(linkedCts.Token);

        using var reader = new StreamReader(stream);

        var contentBuilder = new StringBuilder();

        while (await reader.ReadLineAsync(linkedCts.Token) is { } line)
        {
            if (!line.StartsWith("data: "))
            {
                continue;
            }

            var data = line["data: ".Length..];

            if (data == "[DONE]")
            {
                break;
            }

            using var chunk = JsonDocument.Parse(data);

            var choices = chunk.RootElement.GetProperty("choices");

            if (choices.GetArrayLength() == 0)
            {
                continue;
            }

            var delta = choices[0].GetProperty("delta");

            if (delta.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                contentBuilder.Append(content.GetString());
            }
        }

        var modelContent = contentBuilder.ToString();

        if (string.IsNullOrWhiteSpace(modelContent))
        {
            _logger.LogWarning("AI model returned an empty response.");

            return [];
        }

        var analysis = JsonSerializer.Deserialize<RecommendationAnalysisResult>(
            modelContent,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (analysis?.Recommendations is null ||
            analysis.Recommendations.Count == 0)
        {
            _logger.LogWarning(
                "AI model returned no recommendations. Response: {Response}",
                modelContent);

            return [];
        }

        return analysis.Recommendations;
    }

    private static IReadOnlyList<ProductForecastContext> MapToContexts(
        IReadOnlyList<ProductForecastContext> contexts,
        IReadOnlyList<RecommendationAnalysisItem> recommendations)
    {
        var result = new List<ProductForecastContext>();

        foreach (var context in contexts)
        {
            var recommendation = recommendations.FirstOrDefault(x =>
                    x.ProductId == context.ProductId &&
                    (string.IsNullOrWhiteSpace(x.BranchId) || x.BranchId == context.BranchId));

            if (recommendation is null)
            {
                // Модель пропустила позицию — оставляем расчётное значение.
                result.Add(context with
                {
                    Reason = RecommendationPreparationService.BuildFallbackReason(context)
                });

                continue;
            }

            // Модель может уменьшить количество, но не ниже обязательного минимума
            // и не выше расчётного значения; итог округляется до единицы закупки.
            var lowerBound = Math.Min(context.MinPurchaseQuantity, context.PrecalculatedQuantity);

            var quantity = Math.Clamp(
                recommendation.Quantity,
                lowerBound,
                context.PrecalculatedQuantity);

            quantity = Math.Min(
                RecommendationPreparationService.RoundUpToRatio(quantity, context.PurchaseUnitRatio),
                context.PrecalculatedQuantity);

            if (quantity <= 0)
            {
                continue;
            }

            var reason = string.IsNullOrWhiteSpace(recommendation.Reason)
                ? RecommendationPreparationService.BuildFallbackReason(context)
                : recommendation.Reason;

            if (context.MinPurchaseApplied && quantity == context.PrecalculatedQuantity
                && !reason.Contains("минимум", StringComparison.OrdinalIgnoreCase))
            {
                reason += $" Применён обязательный минимум закупки {context.MinPurchaseQuantity} {context.UnitName}.";
            }

            result.Add(context with
            {
                PrecalculatedQuantity = quantity,
                Reason = reason
            });
        }

        return result;
    }

    private static string BuildSystemPrompt()
    {
        return """
        Ты — помощник по планированию закупок для аптечного склада.

        Проанализируй переданные товары и верни только валидный JSON.
        Не добавляй пояснения до или после JSON.

        ВАЖНО: поле "reason" всегда должно быть на русском языке.
        Не используй английские слова, названия полей, технические термины или английские сокращения в поле "reason".

        Требования к полю "reason":
        - Текст должен быть на русском языке.
        - Текст должен быть исчерпывающим и объяснять реальную причину закупки.
        - Укажи главную причину: прогнозируемая нехватка запаса, сезонный спрос, возможный пик спроса, срок поставки, страховой запас или сочетание этих факторов.
        - Опиши, почему текущего запаса недостаточно для покрытия спроса до целевой даты.
        - Если есть сезонность, укажи это и объясни, как она влияет на потребность.
        - Если есть риск пикового спроса, укажи это и объясни необходимость дополнительного запаса.
        - Если важны срок доставки или страховой запас, упомяни их.
        - Не используй общие формулировки без объяснения причины.
        - Не выдумывай данные, которых нет во входных параметрах.
        - Не упоминай внутренние названия полей, такие как projectedStockAtTargetDate, reorderPoint или precalculatedQuantity.
        - Используй понятные формулировки для сотрудника склада или закупщика.
        - Текст должен содержать от 80 до 200 символов.

        Примеры правильного поля "reason":
        - "Прогнозируемый остаток ниже необходимого уровня из-за ожидаемого сезонного роста спроса; требуется пополнение с учётом срока поставки и страхового запаса."
        - "Текущих запасов недостаточно для покрытия спроса до целевой даты; необходима закупка с учётом срока доставки и страхового запаса."
        - "Ожидается повышенный спрос в связи с сезонностью; требуется пополнение, чтобы избежать дефицита до целевой даты."

        Формат ответа:
        {
          "recommendations": [
            {
              "productId": "string",
              "branchId": "string",
              "targetDate": "YYYY-MM-DD",
              "quantity": 0,
              "reason": "string"
            }
          ]
        }

        Контекст расчёта:
        - Потребность рассчитана на период закупки purchaseFrom–purchaseTo (horizonDays дней).
        - История спроса взята за период analysisFrom–analysisTo.
        - Каждая позиция относится к конкретному филиалу (branchId, branchName).
        - Количество указано в базовой единице unitName; закупка кратна purchaseUnitRatio.

        Правила:
        - Верни запись для каждой пары productId + branchId из входных данных.
        - quantity должна быть целым числом больше 0.
        - quantity не должна превышать precalculatedQuantity.
        - quantity не должна быть меньше minPurchaseQuantity: это обязательный минимум закупки.
        - Если minPurchaseApplied = true, объясни в reason, что закупка выполняется из-за обязательного минимума.
        - branchId должен совпадать с branchId из входных данных.
        - targetDate должен совпадать с переданной целевой датой.
        - productId должен совпадать с productId из входных данных.
        - reason должен быть написан на русском языке и соответствовать требованиям выше.
        """;
    }
}