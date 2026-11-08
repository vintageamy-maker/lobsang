using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Lobsang.Data;

namespace Lobsang.Controllers
{
    /// <summary>
    /// 前台（不用登入）的商品查詢（2026-11-08）：
    ///   GET /api/public/products?q=     關鍵字（名稱、作者、店內碼、條碼）
    ///   GET /api/public/products?code=  用掃到的條碼精確比對（📷 掃碼查詢用）
    /// ⭐ 只回「上架」的商品；價格只給業主自己的售價（查書資料裡的定價、誠品售價在這裡拿掉，業主決定）。
    /// ⭐ 沒給關鍵字也沒給條碼 ⇒ 回空的（前台預設不列出，不讓人一次拿到全部商品）。
    /// ⚠ 不用登入 ⇒ 掛限流（跟搜尋書單同一個規則，以 IP 計）。
    /// </summary>
    [ApiController]
    [Route("api/public/products")]
    public class PublicProductsController(AppDbContext db) : ControllerBase
    {
        [HttpGet]
        [EnableRateLimiting(BookRateLimits.Search)]
        public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? code, [FromQuery] int page = 1,
                                                CancellationToken ct = default)
        {
            const int pageSize = 20;
            page = Math.Max(1, page);
            if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(code))
                return Ok(new { total = 0, page, pageSize, items = Array.Empty<object>() });

            var query = db.Products.AsNoTracking().Where(p => p.IsListed);
            if (!string.IsNullOrWhiteSpace(code))
            {
                var c = code.Replace(" ", "").Replace("-", "");
                // UPC-A 掃到 12 碼、資料存 13 碼（前面補 0）的情況也要對得到
                query = query.Where(p => p.Code == c || p.Code == "0" + c);
            }
            else query = ProductQueries.Filter(query, q);

            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Ok(new
            {
                total, page, pageSize,
                items = rows.Select(p =>
                {
                    var info = p.DataJson is "{}" or "" ? null : BookQueries.ParseInfo(p.DataJson);
                    if (info is not null) { info.Price = null; info.SalePrice = null; }
                    return new
                    {
                        sku = p.Sku, name = p.Name, code = p.Code, midCategory = p.MidCategory, subCategory = p.SubCategory,
                        salePrice = p.SalePrice, stock = p.Stock, authors = p.Authors, info,
                    };
                }),
            });
        }
    }
}
