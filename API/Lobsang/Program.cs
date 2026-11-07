using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Lobsang.Data;
using Lobsang.Services;
using System.Security.Claims;
using System.Text;

namespace Lobsang
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // ⚠ 自訂旗標要先拿掉，否則命令列設定來源會把「--shrink-images」當成缺值的設定鍵
            var shrinkImages = args.Contains("--shrink-images");
            var shrinkApply = args.Contains("--apply");
            args = args.Where(a => a is not "--shrink-images" and not "--apply").ToArray();

            var builder = WebApplication.CreateBuilder(args);

            var connectionString = builder.Configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("缺少連線字串 ConnectionStrings:Default（可用環境變數 ConnectionStrings__Default 提供）");

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<AuditableInterceptor>();
            builder.Services.AddScoped<Services.AuditLogger>();
            builder.Services.AddScoped<Services.ApprovalResolver>();
// 資料可見範圍：Scoped，同一次請求內快取組織樹，別改成 Singleton（會跨機構污染）
builder.Services.AddScoped<Services.DataScopeService>();
// 停用帳號前的簽核殘留檢查與改派（見 UserDeactivationService 類別註解）
builder.Services.AddScoped<Services.UserDeactivationService>();
// LINE ID Token 驗證：綁定與 LIFF 登入共用（⚠ 不可各寫一份）
builder.Services.AddScoped<Services.LineIdentityService>();
// JWT 產生器：⚠ 全系統只有這一份（後台登入與 LINE 短命權杖共用）
builder.Services.AddScoped<Services.TokenIssuer>();

// ===== 檔案儲存 =====
// 後端是**逐機構**決定的（institutions.storage_backend：Local 或 Gcs），故由 Resolver 在請求內解析。
var storageRoot = builder.Configuration["Storage:Local:Root"] ?? "/app/uploads";
builder.Services.AddSingleton(new Services.LocalFileStorage(storageRoot));
builder.Services.AddScoped<Services.FileStorageResolver>();
builder.Services.AddScoped<Services.StepUpService>();

// ===== LINE =====
// ⚠ 佇列是 Singleton（跨請求共用同一個 Channel），處理器是 HostedService。
//    佇列若做成 Scoped，每個請求各拿一個空佇列，事件會靜靜消失。
builder.Services.AddSingleton<Services.LineEventQueue>();
builder.Services.AddSingleton<Services.LineMessagingClient>();
builder.Services.AddHostedService<Services.LineEventProcessor>();
builder.Services.AddHttpClient("line", c => c.Timeout = TimeSpan.FromSeconds(10));
// 查書（Lobsang，2026-11-07）：同時查國圖、Google Books、openBD 等公開資料庫。各來源的逾時在服務內個別設定
builder.Services.AddHttpClient(Services.BookLookup.BookLookupService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(40));
builder.Services.AddScoped<Services.BookLookup.BookLookupService>();

// GCS 為選配：沒有設 bucket、或環境取不到預設憑證時就不註冊。
// ⚠ 不註冊時，設定為雲端的機構會在上傳時**明確失敗**而不是默默改存本機 ——
//    那會讓檔案散落兩處，而且沒有人會發現。
var gcsBucket = builder.Configuration["Storage:Gcs:Bucket"];
if (!string.IsNullOrWhiteSpace(gcsBucket))
{
    try
    {
        var gcsClient = Google.Cloud.Storage.V1.StorageClient.Create();
        builder.Services.AddSingleton(new Services.GcsFileStorage(gcsClient, gcsBucket));
    }
    catch (Exception ex)
    {
        // 啟動時取不到憑證不該讓整個服務起不來 —— 其餘功能與雲端儲存無關
        Console.Error.WriteLine($"[storage] GCS 未啟用：{ex.Message}");
    }
}
            // 錯誤日誌用 Singleton＋IServiceScopeFactory：以獨立交易寫入，
            // 業務交易回滾時日誌仍留得下來
            builder.Services.AddSingleton<Services.ErrorLogger>();
            // 每日清理超過保留期限的錯誤日誌（audit_logs 不在此列，屬業務軌跡）
            builder.Services.AddHostedService<Services.ErrorLogCleanupService>();

            builder.Services.AddDbContext<AppDbContext>((sp, options) =>
                options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
                    .UseSnakeCaseNamingConvention()
                    .AddInterceptors(sp.GetRequiredService<AuditableInterceptor>()));

            // JWT：TLS 由 Cloudflare 終結；issuer/audience 固定驗證
            var jwtKey = builder.Configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("缺少 Jwt:Key 設定（可用環境變數 Jwt__Key 提供）");
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = Services.TokenIssuer.Issuer,
                        ValidateAudience = true,
                        ValidAudience = Services.TokenIssuer.Issuer,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                        ClockSkew = TimeSpan.Zero,
                    };

                    /*
                     * ⚠⚠ LINE 專用權杖（src=line）的第三道鎖：**每一次請求都重驗**
                     *    「這個人還在職，而且還綁著這個 LINE」。
                     *
                     * 既有規則的強度就是「每一次都查 IsActive」（LineEventQueue 也是這樣寫的）。
                     * 發一顆 15 分鐘的權杖等於開了一個 15 分鐘的窗口，那是**降低**既有標準：
                     * 人事在後台把某人停用、或某人自己解除綁定之後，
                     * 那顆權杖照樣能送單，而且完全不會有人發現。
                     *
                     * ⭐ 附帶好處：解除綁定**當下立即失效**，不用等 15 分鐘。
                     * ⚠ 只對 src=line 生效 —— 後台登入的每一個請求都多打一次資料庫是不必要的。
                     */
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async ctx =>
                        {
                            /* ⭐ 憑證版本：users.token_version +1 ⇒ 該人手上的舊憑證立刻作廢（要重新登入拿新權限）。
                             * ⚠ 只查有 tv 的權杖（上線前發的舊權杖沒有，照舊到期為止，避免全院同時被登出）。
                             * ⚠ 用記憶體快取 30 秒：後台每個請求都打一次資料庫是不必要的；30 秒內的落差可以接受。 */
                            if (ctx.Principal?.FindFirst("tv")?.Value is { } tvText
                                && int.TryParse(tvText, out var tv)
                                && int.TryParse(ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var tvUser))
                            {
                                var cache = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                                var current = await Microsoft.Extensions.Caching.Memory.CacheExtensions.GetOrCreateAsync(cache, $"tv:{tvUser}", async e =>
                                {
                                    e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                                    var tvDb = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                                    return await tvDb.Users.Where(u => u.Id == tvUser).Select(u => (int?)u.TokenVersion).FirstOrDefaultAsync();
                                });
                                if (current is null || current != tv) { ctx.Fail("token version changed"); return; }
                            }

                            if (ctx.Principal?.FindFirst("src")?.Value != "line") return;

                            var sub = ctx.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
                            if (!int.TryParse(sub, out var userId)) { ctx.Fail("bad subject"); return; }

                            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                            var stillUsable = await db.Users.AnyAsync(
                                u => u.Id == userId && u.IsActive && u.LineUserId != null);

                            // ⚠ 這裡回 401 而不是 403：權杖本身已經不成立，不是權限不夠。
                            //   LIFF 端收到 401 會自動重換一顆，那時就會拿到
                            //   LineSessionController 明確的「尚未綁定 / 已停用」訊息。
                            if (!stillUsable) ctx.Fail("line binding no longer usable");
                        },
                    };
                });
            builder.Services.AddAuthorization();
            builder.Services.AddMemoryCache();   // 憑證版本檢查的 30 秒快取
            /*
             * 公開端點（不用登入）擋洗版用的限流器。⭐ 新專案要開放匿名表單時，在這裡加 policy，
             *   端點掛 [EnableRateLimiting("名稱")]。⚠ 以 IP 分組、只放記憶體，IP 不寫進資料庫。
             * ⚠ 中介軟體要排在 UseForwardedHeaders 之後才拿得到真正的來源 IP。
             */
            builder.Services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                // 公開查書頁：查外部資料庫每個 IP 每分鐘 20 次（國圖、UPCitemdb 被洗會擋我們）；搜尋書單每分鐘 60 次
                o.AddPolicy(Controllers.BookRateLimits.Lookup, ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
                o.AddPolicy(Controllers.BookRateLimits.Search, ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
            });

            builder.Services.AddControllers(o =>
            {
                // 記錄 4xx 回應（例外由 ExceptionHandlingMiddleware 處理）。
                // order -3000 須早於內建 ModelStateInvalidFilter(-2000)，才攔得到模型驗證失敗
                o.Filters.Add<Filters.ApiErrorLoggingFilter>(-3000);
                /*
                 * ⚠⚠ LINE 專用權杖的路由閘門。理由見 Auth/LineSessionPolicy.cs 檔頭：
                 *    「申請」與「簽核」掛的是**同一個權限碼**，光靠權限碼在 API 層分不開。
                 *    少了這一道，「LINE 不做簽核」就只是「前端沒有按鈕」而已。
                 * ⭐ 對後台登入的權杖零成本：第一行就 return。
                 */
                o.Filters.Add<Auth.LineSessionGuard>();
            }).AddJsonOptions(o =>
            {
                // enum 一律以字串收發（與 DB 存字串一致）
                o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                // TimeOnly 讀取相容 "HH:mm"，輸出固定 "HH:mm"（讀寫格式一致）
                o.JsonSerializerOptions.Converters.Add(new Json.TimeOnlyJsonConverter());
                o.JsonSerializerOptions.Converters.Add(new Json.NullableTimeOnlyJsonConverter());
            });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // 開發時 Vite (5173/5174) 跨來源呼叫；正式環境同源經 nginx 分流
            builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
                p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));


            /*
             * ===== 取得員工真實 IP（X-Forwarded-For）=====
             *
             * ⚠⚠ 沒有這一段時，HttpContext.Connection.RemoteIpAddress 拿到的是
             *    **nginx 容器的內部位址**（172.x），不是員工的 IP。
             *    影響：LINE 綁定紀錄、二次密碼稽核的 ClientIp 全是假的，而且不會報錯。
             *
             * ⚠⚠ 鏈路長度依 Cloudflare 是否代理而不同：
             *
             *    灰雲直連     使用者 → edge → web → api          ⇒ XFF 2 段
             *    橘雲（代理） 使用者 → CF → edge → web → api     ⇒ XFF 3 段
             *
             *    ⚠ 同一份程式要能同時涵蓋。ForwardLimit 取 3，
             *      而較短的那條鏈會在「下一跳不是信任的中繼」時**自己停下來** ——
             *      停止條件是 KnownNetworks，不是跳數，所以放到 3 不會多吃一段。
             *
             * ⚠⚠ 停止條件全靠 KnownNetworks，**清空後一定要加回具體網段**。
             *    留空 ＝ 信任任何來源送來的標頭 ＝ 使用者可自行偽造 IP。
             */
            builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
            {
                // ⚠ 刻意只吃 For，不吃 Proto —— 吃 Proto 會改變 Request.Scheme，
                //    可能觸發非預期的 https 轉址。這裡只需要正確的 IP。
                o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor;
                o.ForwardLimit = 3;

                o.KnownNetworks.Clear();
                o.KnownProxies.Clear();

                foreach (var cidr in TrustedProxyCidrs(builder.Configuration))
                {
                    var slash = cidr.LastIndexOf('/');
                    if (slash <= 0) continue;
                    if (!System.Net.IPAddress.TryParse(cidr[..slash], out var baseIp)) continue;
                    if (!int.TryParse(cidr[(slash + 1)..], out var bits)) continue;
                    // ⚠ IPNetwork 在 .NET 8 有兩個同名型別（System.Net 與 HttpOverrides），
                    //    不寫完整命名空間會編譯失敗；ForwardedHeadersOptions 又在
                    //    Microsoft.AspNetCore.Builder，三者分屬不同命名空間。
                    o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(baseIp, bits));
                }

                // 本機開發（無 proxy 時這段不會生效，加著是為了 docker 以外的情境）
                o.KnownProxies.Add(System.Net.IPAddress.Loopback);
                o.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
            });

            var app = builder.Build();

            /* ⚠⚠ 非正式環境不准連正式庫（源自 SmartCare 2026-09-19 實際事故）：
             *   本機設定檔的連線指向正式庫，本機以 Development 跑起來 ⇒ 下面的 Migrate() 直接改正式庫結構、
             *   DbSeeder 把測試帳號（密碼寫死）種回正式站。只看「環境是不是 Production」不夠，要看「連到哪個庫」。
             * ⭐ 有正式站之後，把正式庫名稱填進設定 Safety:ProductionDatabase（空值＝不檢查）。 */
            var connectedDb = new MySqlConnector.MySqlConnectionStringBuilder(connectionString).Database;
            var productionDb = builder.Configuration["Safety:ProductionDatabase"];
            if (!app.Environment.IsProduction() && !string.IsNullOrWhiteSpace(productionDb)
                && string.Equals(connectedDb, productionDb, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"環境是「{app.Environment.EnvironmentName}」，卻連到正式資料庫 {productionDb} —— 拒絕啟動。");

            // 以 EF Migrations 為 schema 唯一真相；啟動時套用並植入初始資料
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
                // ⚠⚠ 正式環境不種測試帳號。那一批的密碼寫死在程式碼裡且不強制變更，
                //    出現在正式站就是一組已知密碼的高權限後門（SmartCare 2026-09-13 實際出事過）。
                //    判準與 AuthController 的快速登入清單一致：**Production 直接不做**。
                DbSeeder.SeedAsync(db, seedDemoUsers: !app.Environment.IsProduction())
                        .GetAwaiter().GetResult();
            }

            // 一次性工作：舊照片縮小（見 ImageShrinkJob）。做完就結束，不啟動網站。
            if (shrinkImages)
            {
                using var scope = app.Services.CreateScope();
                ImageShrinkJob.RunAsync(scope.ServiceProvider, shrinkApply, Console.Out).GetAwaiter().GetResult();
                return;
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // ⚠ 必須在**所有會讀 IP 的東西之前**，否則讀到的仍是 nginx 的位址。
            //    設定見上方 ForwardedHeadersOptions。
            app.UseForwardedHeaders();

            // 全域例外處理需在最外層，才攔得到後續所有中介軟體與端點的例外
            app.UseMiddleware<Middleware.ExceptionHandlingMiddleware>();

            app.UseCors();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapControllers();

            app.Run();
        }

        /// <summary>
        /// 信任的中繼網段（決定 X-Forwarded-For 可以往回走幾段）。
        ///
        /// ⚠ Cloudflare 的位址範圍**會變動**（清單取自 2026-08-22 的
        /// <c>https://www.cloudflare.com/ips-v4</c>）。因此開放用設定覆寫：
        /// 在 appsettings 或環境變數放 <c>Network:TrustedProxyCidrs</c> 陣列即可，
        /// 不必重新建置。
        ///
        /// ⭐ 萬一清單過期，失敗方式是**看得見的**：稽核紀錄的 IP 會變成 Cloudflare 的位址，
        /// 不會變成靜默採信偽造標頭。
        /// </summary>
        static string[] TrustedProxyCidrs(IConfiguration config)
        {
            var custom = config.GetSection("Network:TrustedProxyCidrs").Get<string[]>();
            if (custom is { Length: > 0 }) return custom;

            return
            [
                // docker 的 bridge 網段（涵蓋 172.16~172.31，本機所有站台）。
                // ⭐ 安全性由部署拓樸保證：api 容器**沒有對外開埠**（只有 edge 開 80/443），
                //    能送標頭進來的只有 nginx。
                "172.16.0.0/12",

                // Cloudflare（走橘雲時會多一段）
                "173.245.48.0/20",  "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
                "141.101.64.0/18",  "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
                "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15",  "104.16.0.0/13",
                "104.24.0.0/14",    "172.64.0.0/13",   "131.0.72.0/22",
            ];
        }
    }
}
