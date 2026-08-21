using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekPeakWidget.Services;

/// <summary>
/// DeepSeek 账户余额查询（GET https://api.deepseek.com/user/balance）。
/// </summary>
public static class DeepSeekApiClient
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>查询余额；失败时抛出异常（401=密钥无效等）。</summary>
    public static async Task<DeepSeekBalance?> GetBalanceAsync(string apiKey)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await _http.SendAsync(req).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<DeepSeekBalance>(json, JsonOpts);
    }
}

/// <summary>余额响应（字段对应官方接口的 snake_case 名称）。</summary>
public class DeepSeekBalance
{
    [JsonPropertyName("is_available")]
    public bool IsAvailable { get; set; }

    [JsonPropertyName("balance_infos")]
    public List<DeepSeekBalanceInfo> BalanceInfos { get; set; } = new();
}

/// <summary>单个币种的余额信息。</summary>
public class DeepSeekBalanceInfo
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("total_balance")]
    public string? TotalBalance { get; set; }

    [JsonPropertyName("granted_balance")]
    public string? GrantedBalance { get; set; }

    [JsonPropertyName("topped_up_balance")]
    public string? ToppedUpBalance { get; set; }
}
