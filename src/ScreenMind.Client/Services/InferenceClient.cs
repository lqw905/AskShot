using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ScreenMind.Client.Models;

namespace ScreenMind.Client.Services;

/// <summary>
/// HTTP client for calling the Python inference service on localhost:8900.
/// </summary>
public class InferenceClient : IDisposable
{
    private readonly HttpClient _http;
    private const string BaseUrl = "http://127.0.0.1:8900";

    public InferenceClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public async Task<bool> IsHealthy()
    {
        try
        {
            var resp = await _http.GetAsync("/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>
    /// Send screenshot directly to VLM for analysis — no OCR preprocessing.
    /// </summary>
    public async Task<AnalyzeResult?> AnalyzeAsync(
        string imageBase64,
        string? userQuestion = null,
        LlmConfig? llmConfig = null)
    {
        llmConfig ??= new LlmConfig();
        var payload = new
        {
            image_base64 = imageBase64,
            user_question = userQuestion,
            api_config = ToApiConfigPayload(llmConfig),
        };

        var resp = await _http.PostAsJsonAsync("/analyze", payload);
        await EnsureSuccessWithBody(resp);
        return await resp.Content.ReadFromJsonAsync<AnalyzeResult>();
    }

    public async Task<ApiConnectionTestResult?> TestLlmConnectionAsync(LlmConfig llmConfig)
    {
        var resp = await _http.PostAsJsonAsync("/config/test", new
        {
            api_config = ToApiConfigPayload(llmConfig),
        });
        await EnsureSuccessWithBody(resp);
        return await resp.Content.ReadFromJsonAsync<ApiConnectionTestResult>();
    }

    public async Task<string?> SaveHistoryAsync(
        string analysis, string userQuestion = "",
        string? screenshotPath = null, string imageHash = "")
    {
        var resp = await _http.PostAsJsonAsync("/history/save", new
        {
            analysis,
            user_question = userQuestion,
            screenshot_path = screenshotPath,
            image_hash = imageHash,
        });
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("id").GetString();
    }

    public async Task<List<HistoryEntry>?> GetRecentAsync(int limit = 10, int hours = 24)
    {
        var resp = await _http.GetAsync($"/history/recent?limit={limit}&hours={hours}");
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<List<HistoryEntry>>(
            result.GetProperty("results").GetRawText());
    }

    public async Task<List<HistoryEntry>?> SearchHistoryAsync(string query, int limit = 10)
    {
        var resp = await _http.PostAsJsonAsync("/history/search", new { query, limit });
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<List<HistoryEntry>>(
            result.GetProperty("results").GetRawText());
    }

    public void Dispose() => _http.Dispose();

    private static object ToApiConfigPayload(LlmConfig config) => new
    {
        endpoint = config.Endpoint.Trim(),
        api_key = config.ApiKey.Trim(),
        model = config.Model.Trim(),
        temperature = config.Temperature,
        max_tokens = config.MaxTokens,
    };

    private static async Task EnsureSuccessWithBody(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;

        var body = await resp.Content.ReadAsStringAsync();
        var message = string.IsNullOrWhiteSpace(body)
            ? resp.ReasonPhrase
            : body;
        throw new HttpRequestException(
            $"HTTP {(int)resp.StatusCode} {resp.StatusCode}: {message}");
    }
}

// ── Response types ─────────────────────────────────────────

public class AnalyzeResult
{
    public string Summary { get; set; } = "";
}

public class ApiConnectionTestResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

public class HistoryEntry
{
    public string Id { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string OcrText { get; set; } = "";
    public string Analysis { get; set; } = "";
    public string UserQuestion { get; set; } = "";
    public string? ScreenshotPath { get; set; }
    public List<string> Tags { get; set; } = [];
    public bool IsFavorite { get; set; }
}
