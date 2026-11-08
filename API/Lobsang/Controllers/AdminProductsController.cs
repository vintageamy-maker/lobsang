using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;
using Lobsang.Services.BookLookup;

namespace Lobsang.Controllers
{
    public record UpdateProductRequest(string Name, string? MidCategory, string? SubCategory, int? SalePrice, int Stock, bool IsListed);
    public record ImportProductRow(string? Sku, string? Name, string? Code, string? MidCategory, string? SubCategory, int? SalePrice, int? Stock);
    public record ImportProductsRequest(List<ImportProductRow> Rows);

    /// <summary>
    /// 商品維護（後台，2026-11-08）：列表、編輯、上架開關、存查書資料、Excel 匯入（以店內碼新增或更新）。
    /// ⭐ 權限先沿用 book.manage（選單「書籍 → 商品」同一個權限）；之後要分開再加 product.manage。
    /// ⭐ Excel 由前端讀檔、先給業主預覽，確認後整批送到 import；後端再驗一次（店內碼、名稱必填，條碼要過檢查碼）。
    /// </summary>
    [ApiController]
    [Route("api/admin/products")]
    [RequirePermission("book.manage")]
    public class AdminProductsController(AppDbContext db, AuditLogger audit) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] string? sub, [FromQuery] string? listed,
                                              [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            var query = ProductQueries.Filter(db.Products.AsNoTracking().Where(p => p.InstitutionId == InstitutionId), q);
            if (!string.IsNullOrWhiteSpace(sub)) query = query.Where(p => p.SubCategory == sub);
            if (listed == "on") query = query.Where(p => p.IsListed);
            if (listed == "off") query = query.Where(p => !p.IsListed);
            var total = await query.CountAsync();
            var ordered = query.OrderBy(p => p.Sku);
            // pageSize=0 ⇒ 全部（匯出、查書補資料用）
            var rows = pageSize <= 0
                ? await ordered.ToListAsync()
                : await ordered.Skip((page - 1) * Math.Min(pageSize, 200)).Take(Math.Min(pageSize, 200)).ToListAsync();
            // 篩選用的子分類清單（不受目前篩選影響）
            var subs = await db.Products.AsNoTracking().Where(p => p.InstitutionId == InstitutionId && p.SubCategory != null)
                .Select(p => p.SubCategory!).Distinct().OrderBy(s => s).ToListAsync();
            var mids = await db.Products.AsNoTracking().Where(p => p.InstitutionId == InstitutionId && p.MidCategory != null)
                .Select(p => p.MidCategory!).Distinct().OrderBy(s => s).ToListAsync();
            return Ok(new { total, subs, mids, items = rows.Select(ProductQueries.ToAdmin) });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, UpdateProductRequest r)
        {
            var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == InstitutionId);
            if (p is null) return NotFound(new { message = "找不到這個商品" });
            if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new { message = "請輸入商品名稱" });
            if (r.SalePrice is < 0 || r.Stock < 0) return BadRequest(new { message = "售價、庫存不能是負數" });
            var before = new { p.Name, p.MidCategory, p.SubCategory, p.SalePrice, p.Stock, p.IsListed };
            p.Name = Cut(r.Name.Trim(), 500)!;
            p.MidCategory = Cut(r.MidCategory?.Trim(), 100);
            p.SubCategory = Cut(r.SubCategory?.Trim(), 100);
            p.SalePrice = r.SalePrice;
            p.Stock = r.Stock;
            p.IsListed = r.IsListed;
            audit.Log("product.update", "product", p.Id, before, new { p.Name, p.MidCategory, p.SubCategory, p.SalePrice, p.Stock, p.IsListed });
            await db.SaveChangesAsync();
            return Ok(ProductQueries.ToAdmin(p));
        }

        /// <summary>存查書補到的資料（前端查完：伺服器查書＋瀏覽器查誠品，合併後送來）</summary>
        [HttpPut("{id:int}/info")]
        public async Task<IActionResult> SaveInfo(int id, BookInfo info)
        {
            var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == InstitutionId);
            if (p is null) return NotFound(new { message = "找不到這個商品" });
            var people = info.Kind == "product" ? (info.Brand is null ? [] : [info.Brand]) : info.Authors;
            p.Authors = Cut(people.Count > 0 ? string.Join("、", people) : null, 500);
            p.DataJson = JsonSerializer.Serialize(info, PublicBooksController.Json);
            await db.SaveChangesAsync();
            return Ok(ProductQueries.ToAdmin(p));
        }

        /// <summary>Excel 匯入：店內碼已存在就更新，不存在就新增。有問題的列略過並回報。</summary>
        [HttpPost("import")]
        public async Task<IActionResult> Import(ImportProductsRequest req)
        {
            if (req.Rows is null || req.Rows.Count == 0) return BadRequest(new { message = "沒有資料" });
            if (req.Rows.Count > 5000) return BadRequest(new { message = "一次最多匯入 5000 筆" });
            var existing = await db.Products.Where(p => p.InstitutionId == InstitutionId).ToDictionaryAsync(p => p.Sku);
            int added = 0, updated = 0;
            var errors = new List<object>();
            var seen = new HashSet<string>();
            foreach (var r in req.Rows)
            {
                var sku = r.Sku?.Trim() ?? "";
                var name = r.Name?.Trim() ?? "";
                var code = (r.Code ?? "").Replace(" ", "").Replace("-", "");
                string? problem = sku == "" ? "沒有店內碼" : !seen.Add(sku) ? "店內碼重複" : name == "" ? "沒有商品名稱"
                    : code != "" && !ProductQueries.IsEanOk(code) ? "條碼檢查碼不對"
                    : r.SalePrice is < 0 || r.Stock is < 0 ? "售價或庫存是負數" : null;
                if (problem is not null) { errors.Add(new { sku, name, message = problem }); continue; }

                if (!existing.TryGetValue(sku, out var p))
                {
                    p = new Product { InstitutionId = InstitutionId, Sku = Cut(sku, 30)!, IsListed = true };
                    db.Products.Add(p);
                    existing[sku] = p;
                    added++;
                }
                else updated++;
                // 條碼換了，舊的查書資料就不能用
                if (p.Code != code) { p.DataJson = "{}"; p.Authors = null; }
                p.Name = Cut(name, 500)!;
                p.Code = code;
                if (!string.IsNullOrWhiteSpace(r.MidCategory)) p.MidCategory = Cut(r.MidCategory.Trim(), 100);
                if (!string.IsNullOrWhiteSpace(r.SubCategory)) p.SubCategory = Cut(r.SubCategory.Trim(), 100);
                if (r.SalePrice is not null) p.SalePrice = r.SalePrice;
                if (r.Stock is not null) p.Stock = r.Stock.Value;
            }
            audit.Log("product.import", "product", null, null, new { added, updated, errors = errors.Count });
            await db.SaveChangesAsync();
            return Ok(new { added, updated, errors });
        }

        /// <summary>
        /// 刪除商品（可一次多筆，2026-11-08 業主：真的刪掉、要先確認）。
        /// ⭐ 異動紀錄留下刪掉的店內碼、名稱、條碼；採購單上的紀錄保留（明細的 ProductId 會對不到，畫面顯示「商品已刪除」）。
        /// </summary>
        [HttpPost("delete")]
        public async Task<IActionResult> Delete(DeleteProductsRequest req)
        {
            if (req.Ids is null || req.Ids.Count == 0) return BadRequest(new { message = "請先勾選要刪除的商品" });
            var rows = await db.Products.Where(p => p.InstitutionId == InstitutionId && req.Ids.Contains(p.Id)).ToListAsync();
            if (rows.Count == 0) return NotFound(new { message = "找不到要刪除的商品" });
            db.Products.RemoveRange(rows);
            audit.Log("product.delete", "product", rows.Count == 1 ? rows[0].Id : null,
                      rows.Select(p => new { p.Id, p.Sku, p.Name, p.Code }), null);
            await db.SaveChangesAsync();
            return Ok(new { count = rows.Count });
        }

        static string? Cut(string? s, int max) => string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];
    }

    public record DeleteProductsRequest(List<int> Ids);

    internal static class ProductQueries
    {
        /// <summary>關鍵字比對：店內碼、條碼、名稱、作者（空白分隔的每個字都要對到）</summary>
        public static IQueryable<Product> Filter(IQueryable<Product> query, string? q)
        {
            if (string.IsNullOrWhiteSpace(q)) return query;
            foreach (var raw in q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5))
            {
                var w = raw.Replace("-", "");
                query = query.Where(p => p.Sku.Contains(raw) || p.Code.Contains(w) || p.Name.Contains(raw)
                                         || (p.Authors != null && p.Authors.Contains(raw)));
            }
            return query;
        }

        public static object ToAdmin(Product p) => new
        {
            id = p.Id, sku = p.Sku, name = p.Name, code = p.Code, midCategory = p.MidCategory, subCategory = p.SubCategory,
            salePrice = p.SalePrice, stock = p.Stock, isListed = p.IsListed, updatedAt = p.UpdatedAt,
            info = p.DataJson is "{}" or "" ? null : BookQueries.ParseInfo(p.DataJson),
        };

        /// <summary>EAN-8／UPC-A／EAN-13 檢查碼</summary>
        public static bool IsEanOk(string s)
        {
            if (!(s.Length is 8 or 12 or 13) || !s.All(char.IsAsciiDigit)) return false;
            var d = s.PadLeft(13, '0').Select(c => c - '0').ToArray();
            var sum = 0;
            for (var i = 0; i < 12; i++) sum += d[i] * (i % 2 == 1 ? 3 : 1);
            return (sum + d[12]) % 10 == 0;
        }
    }
}
