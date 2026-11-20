using System.Net.Http.Json;
using UI.Models;

namespace UI.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResult<T>> GetPaged<T>(
        string endpoint,
        int page,
        int pageSize,
        string? search = null,
        string? sortBy = null,
        bool sortDescending = false,
        string? branchId = null)
    {
        var url = $"{endpoint}/paged?page={page}&pageSize={pageSize}";

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            url += $"&branchId={Uri.EscapeDataString(branchId)}";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            url += $"&search={Uri.EscapeDataString(search)}";
        }

        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            url += $"&sortBy={Uri.EscapeDataString(sortBy)}" +
                   $"&sortDescending={sortDescending.ToString().ToLowerInvariant()}";
        }

        return await _http.GetFromJsonAsync<PagedResult<T>>(url)
            ?? new PagedResult<T>();
    }

    public async Task<List<T>> GetAll<T>(string endpoint)
    {
        return await _http.GetFromJsonAsync<List<T>>($"{endpoint}") ?? [];
    }

    public async Task<T?> GetById<T>(string endpoint, string id)
    {
        return await _http.GetFromJsonAsync<T>($"{endpoint}/{id}");
    }

    public async Task Create<T>(string endpoint, T item)
    {
        var response = await _http.PostAsJsonAsync(endpoint, item);
        await EnsureSuccessAsync(response);
    }

    public async Task Update<T>(string endpoint, string id, T item)
    {
        var response = await _http.PutAsJsonAsync($"{endpoint}/{id}", item);
        await EnsureSuccessAsync(response);
    }

    public async Task Delete(string endpoint, string id)
    {
        var response = await _http.DeleteAsync($"{endpoint}/{id}");
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteMany(string endpoint, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            await Delete(endpoint, id);
        }
    }

    public async Task<RecommendationAnalysisStatus> GetAnalysisStatusAsync()
    {
        return await _http.GetFromJsonAsync<RecommendationAnalysisStatus>(
            "api/recommendations/analysis/status")
            ?? new RecommendationAnalysisStatus();
    }

    public async Task AnalyzeAsync(RecommendationAnalysisRequest request)
    {
        var response = await _http.PostAsJsonAsync(
            "api/recommendations/analysis",
            request);

        await EnsureSuccessAsync(response);
    }

    public async Task<List<BranchRecommendationSummary>> GetBranchSummaryAsync()
    {
        return await _http.GetFromJsonAsync<List<BranchRecommendationSummary>>(
            "api/recommendations/analysis/branches") ?? [];
    }

    /// <summary>
    /// Пробрасывает текст ошибки API (например, «Филиал используется…»), а не только код.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await response.Content.ReadAsStringAsync();

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(message)
                ? $"Ошибка API: {(int)response.StatusCode} {response.ReasonPhrase}"
                : message.Trim('"'),
            null,
            response.StatusCode);
    }

    public async Task<IntegrationSettings> GetSettingsAsync()
    {
        return await _http.GetFromJsonAsync<IntegrationSettings>("api/settings")
               ?? new IntegrationSettings();
    }

    public async Task SaveSettingsAsync(IntegrationSettings settings)
    {
        var response = await _http.PutAsJsonAsync("api/settings", settings);
        await EnsureSuccessAsync(response);
    }

    public async Task ImportNowAsync(string entityType)
    {
        var response = await _http.PostAsync(
            $"api/settings/import/{entityType}",
            null);

        response.EnsureSuccessStatusCode();
    }
}