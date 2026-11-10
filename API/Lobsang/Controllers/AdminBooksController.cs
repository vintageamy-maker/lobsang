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
    public record SaveBookRequest(BookInfo Info, string? Note);
    public record SaveBookNoteRequest(string? Note);

    /// <summary>
    /// 書單維護（後台）：查書、加入書單、改備註、刪除、匯出。
    /// ⭐ 後台的查書不限流（要登入且有 book.manage 才進得來），掃書建清單時才不會一直被擋。
    /// </summary>
    [ApiController]
    [Route("api/admin/books")]
    [RequirePermission("book.manage")]
    public class AdminBooksController(AppDbContext db, AuditLogger audit, BookLookupService lookup) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            page = Math.Max(1, page);
            // pageSize=0 ⇒ 全部（匯出 CSV 用）
            var query = BookQueries.Filter(db.Books.AsNoTracking().Where(b => b.InstitutionId == InstitutionId), q);
            var total = await query.CountAsync();
            var ordered = query.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id);
            var rows = pageSize <= 0
                ? await ordered.ToListAsync()
                : await ordered.Skip((page - 1) * Math.Min(pageSize, 200)).Take(Math.Min(pageSize, 200)).ToListAsync();

            // 2026-11-10 業主：採購清單表格標示「已在商品清單」（條碼一樣；UPC 存 13 碼前面補 0 的也算）
            var codes = rows.Select(b => b.Code).Distinct().ToList();
            var codes0 = codes.Select(c => "0" + c).ToList();
            var inProducts = (await db.Products.AsNoTracking()
                    .Where(p => p.InstitutionId == InstitutionId && (codes.Contains(p.Code) || codes0.Contains(p.Code)))
                    .Select(p => new { p.Code, p.Sku, p.Stock, p.IsListed }).ToListAsync())
                .GroupBy(p => p.Code.Length == 13 && p.Code.StartsWith('0') && !codes.Contains(p.Code) ? p.Code[1..] : p.Code)
                .ToDictionary(g => g.Key, g => g.First());

            return Ok(new
            {
                total,
                items = rows.Select(b => new
                {
                    id = b.Id, code = b.Code, kind = b.Kind, title = b.Title, authors = b.Authors,
                    publisher = b.Publisher, pubDate = b.PubDate, note = b.Note, createdAt = b.CreatedAt,
                    info = BookQueries.ParseInfo(b.DataJson),
                    product = inProducts.TryGetValue(b.Code, out var p) ? new { sku = p.Sku, stock = p.Stock, isListed = p.IsListed } : null,
                }),
            });
        }

        [HttpGet("lookup")]
        public async Task<IActionResult> Lookup([FromQuery] string? code, CancellationToken ct)
        {
            var r = await lookup.LookupAsync(code, ct);
            var existing = r.Code is null ? null
                : await db.Books.AsNoTracking().Where(b => b.InstitutionId == InstitutionId && b.Code == r.Code)
                    .Select(b => new { b.Id, b.Note }).FirstOrDefaultAsync(ct);
            // 2026-11-10 業主：查到的書已經在商品清單（已經在賣）就提示，避免重複採購；自動加入／連續掃描也不加
            //   UPC-A 存成 13 碼（前面補 0）的也要對得到
            var product = r.Code is null ? null
                : await db.Products.AsNoTracking()
                    .Where(p => p.InstitutionId == InstitutionId && (p.Code == r.Code || p.Code == "0" + r.Code))
                    .Select(p => new { id = p.Id, sku = p.Sku, name = p.Name, stock = p.Stock, isListed = p.IsListed })
                    .FirstOrDefaultAsync(ct);
            return Ok(new { result = r, existingId = existing?.Id, existingNote = existing?.Note, product });
        }

        /// <summary>加入書單；同一個條碼已經在書單裡就更新資料（備註有給才覆蓋）</summary>
        [HttpPost]
        public async Task<IActionResult> Save(SaveBookRequest request)
        {
            var info = request.Info;
            var parsed = CodeParser.Parse(info?.Code);
            if (info is null || parsed.Code is null || parsed.Kind is not ("book" or "product"))
                return BadRequest(new { message = "條碼不正確，只能加入書或商品。" });
            info.Code = parsed.Code;
            info.Kind = parsed.Kind;

            var book = await db.Books.FirstOrDefaultAsync(b => b.InstitutionId == InstitutionId && b.Code == info.Code);
            var isNew = book is null;
            book ??= new Book { InstitutionId = InstitutionId, Code = info.Code };
            book.Kind = info.Kind;
            book.Title = Cut(info.Title, 500) ?? "（無名稱）";
            var people = info.Kind == "product" ? (info.Brand is null ? [] : [info.Brand]) : info.Authors;
            book.Authors = Cut(people.Count > 0 ? string.Join("、", people) : null, 500);
            book.Publisher = Cut(info.Kind == "product" ? info.Quantity : info.Publisher, 300);
            book.PubDate = Cut(info.Date, 50);
            if (request.Note is not null) book.Note = Cut(request.Note.Trim(), 1000);
            book.DataJson = JsonSerializer.Serialize(info, PublicBooksController.Json);

            if (isNew) db.Books.Add(book);
            audit.Log(isNew ? "book.create" : "book.update", "book", book.Id == 0 ? null : book.Id, null,
                      new { book.Code, book.Title, book.Note });
            await db.SaveChangesAsync();
            return Ok(new { id = book.Id, isNew });
        }

        [HttpPut("{id:int}/note")]
        public async Task<IActionResult> SaveNote(int id, SaveBookNoteRequest request)
        {
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id && b.InstitutionId == InstitutionId);
            if (book is null) return NotFound();
            var before = book.Note;
            book.Note = Cut(request.Note?.Trim(), 1000);
            audit.Log("book.note", "book", id, new { note = before }, new { note = book.Note });
            await db.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var book = await db.Books.FirstOrDefaultAsync(b => b.Id == id && b.InstitutionId == InstitutionId);
            if (book is null) return NotFound();
            db.Books.Remove(book);
            audit.Log("book.delete", "book", id, new { book.Code, book.Title, book.Note }, null);
            await db.SaveChangesAsync();
            return Ok();
        }

        static string? Cut(string? s, int max) =>
            string.IsNullOrWhiteSpace(s) ? null : s.Length <= max ? s : s[..max];
    }
}
