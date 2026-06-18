using System.IO;
using System.Text.Json;

namespace AskShot.Client.Models;

/// <summary>
/// Application configuration, persisted to appsettings.json.
/// </summary>
public class AppConfig
{
    public LlmConfig Llm { get; set; } = new();
    public HotkeyConfig Hotkeys { get; set; } = new();
    public DataConfig Data { get; set; } = new();

    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AskShot");

    public static string DataDir { get; } = Path.Combine(AppDataDir, "data");
    public static string LogsDir { get; } = Path.Combine(AppDataDir, "logs");

    private static readonly string ConfigPath = Path.Combine(AppDataDir, "appsettings.json");
    private static readonly string LegacyConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig Load()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);

        var path = File.Exists(ConfigPath) ? ConfigPath : LegacyConfigPath;
        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppConfig();

            ApplyLegacyLlmFields(config, json);
            if (path == LegacyConfigPath)
                config.Save();
            return config;
        }
        return new AppConfig();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(ConfigPath, json);
    }

    private static void ApplyLegacyLlmFields(AppConfig config, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Llm", out var llm)) return;

            config.Llm.Endpoint = GetString(llm, "Endpoint", "endpoint") ?? config.Llm.Endpoint;
            config.Llm.ApiKey = GetString(llm, "ApiKey", "api_key") ?? config.Llm.ApiKey;
            config.Llm.Model = GetString(llm, "Model", "model") ?? config.Llm.Model;
            config.Llm.Temperature = GetSingle(llm, "Temperature", "temperature") ?? config.Llm.Temperature;
            config.Llm.MaxTokens = GetInt32(llm, "MaxTokens", "max_tokens") ?? config.Llm.MaxTokens;
        }
        catch
        {
            // If migration parsing fails, keep the normally deserialized config.
        }
    }

    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static float? GetSingle(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.TryGetSingle(out var result))
                return result;
        return null;
    }

    private static int? GetInt32(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result))
                return result;
        return null;
    }
}

public class LlmConfig
{
    public string Endpoint { get; set; } = "";  // 空 = Mock 模式
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "qwen3-vl-flash";
    public float Temperature { get; set; } = 0.7f;
    public int MaxTokens { get; set; } = 2048;
}

public class HotkeyConfig
{
    public string CaptureAndAnalyze { get; set; } = "Ctrl+Shift+A";
}

public class DataConfig
{
    public bool SaveScreenshots { get; set; } = false;
    public string ScreenshotPath { get; set; } = "";
    public int HistoryRetentionDays { get; set; } = 30;
}
