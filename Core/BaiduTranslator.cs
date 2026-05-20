using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScreenTranslator.Core;

/// <summary>
/// 百度通用翻译 API 封装（含缓存）
/// </summary>
internal sealed class BaiduTranslator : IDisposable
{
    private readonly string _appId;
    private readonly string _secretKey;
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, string> _cache = new();
    private readonly object _cacheLock = new();
    private const string BaseUrl = "https://fanyi-api.baidu.com/api/trans/vip/translate";

    /// <summary>缓存最大条目数</summary>
    internal int MaxCacheSize { get; set; } = 5000;

    /// <summary>API 调用间隔（毫秒，防止限流）</summary>
    internal int ApiDelayMs { get; set; } = 200;

    private DateTime _lastApiCall = DateTime.MinValue;

    internal BaiduTranslator(string appId, string secretKey)
    {
        _appId = appId;
        _secretKey = secretKey;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    /// <summary>
    /// 翻译单句（带缓存）
    /// </summary>
    internal async Task<string> TranslateAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // 检查缓存
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(text, out var cached))
                return cached;
        }

        // 调用 API
        try
        {
            // 限流控制
            var elapsed = (DateTime.Now - _lastApiCall).TotalMilliseconds;
            if (elapsed < ApiDelayMs)
                await Task.Delay(ApiDelayMs - (int)elapsed);

            var result = await CallBaiduApiAsync(text);
            _lastApiCall = DateTime.Now;

            // 写入缓存
            lock (_cacheLock)
            {
                if (_cache.Count >= MaxCacheSize)
                    _cache.Clear(); // 缓存满则清空
                _cache[text] = result;
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Error($"翻译失败: {ex.Message}", ex);
            return $"[{text}]"; // 失败时返回原文加标记
        }
    }

    /// <summary>
    /// 批量翻译（合并多个短文本以提升效率）
    /// </summary>
    internal async Task<string[]> TranslateBatchAsync(string[] texts)
    {
        if (texts == null || texts.Length == 0)
            return Array.Empty<string>();

        var results = new string[texts.Length];
        var uncached = new List<(int index, string text)>();

        // 先查缓存
        lock (_cacheLock)
        {
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && _cache.TryGetValue(texts[i], out var cached))
                {
                    results[i] = cached;
                }
                else
                {
                    uncached.Add((i, texts[i] ?? string.Empty));
                }
            }
        }

        if (uncached.Count == 0)
            return results;

        // 合并短文本用 \n 分隔，一次 API 调用翻译多条
        var batchText = string.Join("\n", uncached.Select(u => u.text));

        try
        {
            var translated = await TranslateAsync(batchText);
            var lines = translated.Split('\n', StringSplitOptions.None);

            for (int i = 0; i < uncached.Count && i < lines.Length; i++)
            {
                var (index, original) = uncached[i];
                results[index] = lines[i].Trim();

                // 写缓存
                lock (_cacheLock)
                {
                    if (_cache.Count < MaxCacheSize)
                        _cache[original] = results[index];
                }
            }
        }
        catch
        {
            // 批量失败，逐个尝试
            foreach (var (index, original) in uncached)
            {
                try
                {
                    results[index] = await TranslateAsync(original);
                }
                catch
                {
                    results[index] = $"[{original}]";
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 调用百度翻译 API
    /// </summary>
    private async Task<string> CallBaiduApiAsync(string text)
    {
        var salt = DateTime.Now.Ticks.ToString();
        var sign = ComputeMd5($"{_appId}{text}{salt}{_secretKey}");

        var parameters = new Dictionary<string, string>
        {
            { "q", text },
            { "from", "en" },
            { "to", "zh" },
            { "appid", _appId },
            { "salt", salt },
            { "sign", sign },
        };

        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(BaseUrl, content);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            Logger.Error($"百度翻译 API HTTP 错误: {response.StatusCode}, 响应: {json}");
            response.EnsureSuccessStatusCode();
        }

        return ParseResponse(json);
    }

    /// <summary>
    /// 解析 API 响应 JSON
    /// </summary>
    private static string ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // 检查错误
        if (doc.RootElement.TryGetProperty("error_code", out var errorCode))
        {
            var msg = doc.RootElement.TryGetProperty("error_msg", out var errorMsg)
                ? errorMsg.GetString()
                : "未知错误";
            Logger.Error($"百度翻译 API 错误 [{errorCode}]: {msg}");
            throw new InvalidOperationException($"百度翻译 API 错误 [{errorCode}]: {msg}");
        }

        // 解析翻译结果
        var transResult = doc.RootElement.GetProperty("trans_result");
        var sb = new StringBuilder();
        bool first = true;

        foreach (var item in transResult.EnumerateArray())
        {
            if (!first) sb.Append('\n');
            sb.Append(item.GetProperty("dst").GetString());
            first = false;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 计算 MD5 签名
    /// </summary>
    private static string ComputeMd5(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 清空翻译缓存
    /// </summary>
    internal void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
