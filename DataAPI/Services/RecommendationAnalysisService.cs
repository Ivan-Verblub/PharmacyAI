using DataAPI.Services;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DataAPI.Services;

public record RecommendationAnalysisResult(
    IReadOnlyList<RecommendationAnalysisItem> Recommendations);

public record RecommendationAnalysisItem(
    string ProductId,
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
    }

    public async Task<IReadOnlyList<ProductForecastContext>> AnalyzeAsync(
        RecommendationPrompt prompt,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = "ollama";

        //if (string.IsNullOrWhiteSpace(apiKey))
        //{
        //    _logger.LogWarning(
        //        "OpenAI API key is not configured. Returning precalculated recommendations.");

        //    return prompt.Products;
        //}

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
                        serviceLevelZ = prompt.ServiceLevelZ,
                        products = prompt.Products
                    })
                }
            },
            response_format = new
            {
                type = "json_object"
            },
            max_completion_tokens = 2000
        };
        _httpClient.BaseAddress = new Uri("http://localhost:11434/v1/");
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "chat/completions");

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
            .ReadAsStreamAsync(cancellationToken);

        using var reader = new StreamReader(stream);

        var contentBuilder = new StringBuilder();
        var receivedChunks = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
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

            receivedChunks++;

            if (receivedChunks % 5 == 0)
            {
                progress?.Report(Math.Min(90, 25 + receivedChunks));
            }
        }

        progress?.Report(95);

        var modelContent = contentBuilder.ToString();

        if (string.IsNullOrWhiteSpace(modelContent))
        {
            _logger.LogWarning("OpenAI returned an empty response.");

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
                "OpenAI returned no recommendations. Response: {Response}",
                modelContent);

            return [];
        }

        return MapToContexts(prompt, analysis.Recommendations);
    }

    private static IReadOnlyList<ProductForecastContext> MapToContexts(
        RecommendationPrompt prompt,
        IReadOnlyList<RecommendationAnalysisItem> recommendations)
    {
        var result = new List<ProductForecastContext>();

        foreach (var recommendation in recommendations)
        {
            var context = prompt.Products
                .FirstOrDefault(x => x.ProductId == recommendation.ProductId);

            if (context is null)
            {
                continue;
            }

            var quantity = Math.Clamp(
                recommendation.Quantity,
                0,
                context.PrecalculatedQuantity);

            if (quantity <= 0)
            {
                continue;
            }

            result.Add(context with
            {
                PrecalculatedQuantity = quantity,
                Reason = recommendation.Reason
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
              "targetDate": "YYYY-MM-DD",
              "quantity": 0,
              "reason": "string"
            }
          ]
        }

        Правила:
        - Оставляй только товары, для которых projectedStockAtTargetDate меньше reorderPoint.
        - quantity должна быть целым числом больше 0.
        - quantity не должна превышать precalculatedQuantity.
        - targetDate должен совпадать с переданной целевой датой.
        - productId должен совпадать с productId из входных данных.
        - reason должен быть написан на русском языке и соответствовать требованиям выше.
        """;
    }
}