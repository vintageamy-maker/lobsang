using System.Text.Json;

namespace Lobsang.Services
{
    /// <summary>
    /// 驗證 LIFF 前端給的 LINE ID Token，換回該使用者在本 channel 的 userId。
    ///
    /// ⚠ 綁定（LineBindController）與 LIFF 登入（LineSessionController）共用這一份 ——
    ///    **不可複製貼上**：身分驗證有兩份就會有一份先過期。
    ///
    /// ⚠ **刻意不自己解 JWT 驗簽章**。自己驗要處理簽章演算法、金鑰輪替、
    ///    exp／aud／iss 各項檢查，任一項寫漏都會變成「看起來有驗其實沒驗」。
    ///    官方端點一次把這些做完，而且金鑰輪替不用我們跟。
    /// </summary>
    public class LineIdentityService(
        IConfiguration config, IHttpClientFactory http, ILogger<LineIdentityService> logger)
    {
        /// <summary>
        /// 驗 ID Token。成功回 (lineUserId, null)，失敗回 (null, 給人看的原因)。
        /// </summary>
        public async Task<(string? LineUserId, string? Error)> VerifyAsync(string? idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken)) return (null, "沒有取得 LINE 身分證明");

            var clientId = ResolveClientId();
            if (string.IsNullOrWhiteSpace(clientId)) return (null, "伺服器未設定 LINE Login channel ID");

            try
            {
                var client = http.CreateClient("line");
                using var res = await client.PostAsync(
                    "https://api.line.me/oauth2/v2.1/verify",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["id_token"] = idToken,
                        ["client_id"] = clientId,
                    }));

                var text = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                    return (null, $"LINE 回應 {(int)res.StatusCode}");

                using var doc = JsonDocument.Parse(text);
                // sub ＝ 該使用者在這個 channel 的 userId
                return doc.RootElement.TryGetProperty("sub", out var sub)
                    ? (sub.GetString(), null)
                    : (null, "LINE 回應缺少使用者識別");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "驗證 LINE ID Token 失敗");
                return (null, "無法連線至 LINE 驗證服務");
            }
        }

        /// <summary>
        /// client_id 是 **LINE Login channel 的 channel ID**，
        /// 也就是 LIFF ID 的前半段（<c>1234567890-AbCdEfGh</c> 的 <c>1234567890</c>）。
        /// 可用 Line:LoginChannelId 覆寫。
        /// </summary>
        string? ResolveClientId()
        {
            var clientId = config["Line:LoginChannelId"];
            if (!string.IsNullOrWhiteSpace(clientId)) return clientId;

            var liffId = config["Line:LiffId"] ?? "";
            var dash = liffId.IndexOf('-');
            return dash > 0 ? liffId[..dash] : null;
        }
    }
}
