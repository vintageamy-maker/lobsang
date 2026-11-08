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
    public record SubmitPurchaseLine(int BookId, int Quantity);
    public record SubmitPurchaseRequest(List<SubmitPurchaseLine> Lines, string? Note);
    public record DecidePurchaseRequest(List<int> ItemIds, bool Approve, string? Reason);
    public record ListPurchaseItemRequest(string Sku, string Name, string? MidCategory, string? SubCategory, int? SalePrice, int Stock, bool IsListed);

    /// <summary>
    /// 採購審核（採購候選的第二階段，2026-11-08）。
    ///   POST /api/admin/purchase-orders                       送審：從採購清單勾幾本＋數量 → 建採購單，書從清單移走
    ///   GET  /api/admin/purchase-orders                       採購單列表（含各狀態本數）
    ///   GET  /api/admin/purchase-orders/{id}                  採購單明細
    ///   POST /api/admin/purchase-orders/{id}/decide           核准／退回（purchase.approve）；退回的書回到採購清單
    ///   POST /api/admin/purchase-orders/items/{itemId}/list   核准的「上架」：填店內碼等 → 建商品
    /// ⭐ 送審、上架用 book.manage；核准／退回另外要 purchase.approve（業主決定先只給系統管理員）。
    /// </summary>
    [ApiController]
    [Route("api/admin/purchase-orders")]
    [RequirePermission("book.manage")]
    public class AdminPurchaseOrdersController(AppDbContext db, AuditLogger audit) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);
        private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpPost]
        public async Task<IActionResult> Submit(SubmitPurchaseRequest req)
        {
            if (req.Lines is null || req.Lines.Count == 0) return BadRequest(new { message = "請先勾選要送審的書" });
            if (req.Lines.Any(l => l.Quantity < 1 || l.Quantity > 9999)) return BadRequest(new { message = "採購數量要是 1～9999" });
            var ids = req.Lines.Select(l => l.BookId).Distinct().ToList();
            var books = await db.Books.Where(b => b.InstitutionId == InstitutionId && ids.Contains(b.Id)).ToListAsync();
            if (books.Count != ids.Count) return BadRequest(new { message = "有些書已經不在採購清單裡（可能別人剛送審），請重新整理" });

            var now = DateTime.UtcNow;
            var order = new PurchaseOrder
            {
                InstitutionId = InstitutionId, OrderNo = await NextOrderNoAsync(now), SubmittedBy = UserId, SubmittedAt = now,
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim()[..Math.Min(req.Note.Trim().Length, 1000)],
            };
            foreach (var l in req.Lines)
            {
                var b = books.First(x => x.Id == l.BookId);
                order.Items.Add(new PurchaseOrderItem
                {
                    Code = b.Code, Kind = b.Kind, Title = b.Title, Authors = b.Authors, Publisher = b.Publisher,
                    PubDate = b.PubDate, Note = b.Note, DataJson = b.DataJson, Quantity = l.Quantity,
                });
            }
            db.PurchaseOrders.Add(order);
            db.Books.RemoveRange(books);   // 業主：送審後從採購清單移到採購單
            await db.SaveChangesAsync();
            audit.Log("purchase.submit", "purchase_order", order.Id, null,
                      new { order.OrderNo, items = order.Items.Select(i => new { i.Code, i.Title, i.Quantity }) });
            await db.SaveChangesAsync();
            return Ok(new { id = order.Id, orderNo = order.OrderNo });
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var orders = await db.PurchaseOrders.AsNoTracking().Where(o => o.InstitutionId == InstitutionId)
                .OrderByDescending(o => o.SubmittedAt).ThenByDescending(o => o.Id)
                .Select(o => new
                {
                    o.Id, o.OrderNo, o.SubmittedBy, o.SubmittedAt, o.Note,
                    count = o.Items.Count, quantity = o.Items.Sum(i => i.Quantity),
                    pending = o.Items.Count(i => i.Status == PurchaseItemStatus.Pending),
                    approved = o.Items.Count(i => i.Status == PurchaseItemStatus.Approved),
                    rejected = o.Items.Count(i => i.Status == PurchaseItemStatus.Rejected),
                    listed = o.Items.Count(i => i.Status == PurchaseItemStatus.Listed),
                }).ToListAsync();
            var names = await UserNamesAsync(orders.Select(o => o.SubmittedBy));
            return Ok(orders.Select(o => new
            {
                id = o.Id, orderNo = o.OrderNo, submittedAt = o.SubmittedAt, submittedBy = names.GetValueOrDefault(o.SubmittedBy, "—"),
                note = o.Note, o.count, o.quantity, o.pending, o.approved, o.rejected, o.listed,
            }));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var o = await db.PurchaseOrders.AsNoTracking().Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == InstitutionId);
            if (o is null) return NotFound(new { message = "找不到這張採購單" });
            var names = await UserNamesAsync(o.Items.Where(i => i.DecidedBy != null).Select(i => i.DecidedBy!.Value).Append(o.SubmittedBy));
            // 上架後商品可能被刪掉（2026-11-08 商品可刪除）：查一下還在不在，畫面顯示「商品已刪除」
            var listedIds = o.Items.Where(i => i.ProductId != null).Select(i => i.ProductId!.Value).ToList();
            var alive = await db.Products.AsNoTracking().Where(p => listedIds.Contains(p.Id)).Select(p => p.Id).ToListAsync();
            return Ok(new
            {
                id = o.Id, orderNo = o.OrderNo, submittedAt = o.SubmittedAt, submittedBy = names.GetValueOrDefault(o.SubmittedBy, "—"), note = o.Note,
                items = o.Items.OrderBy(i => i.Id).Select(i => new
                {
                    id = i.Id, code = i.Code, kind = i.Kind, title = i.Title, authors = i.Authors, publisher = i.Publisher,
                    pubDate = i.PubDate, note = i.Note, quantity = i.Quantity, status = i.Status, rejectReason = i.RejectReason,
                    decidedBy = i.DecidedBy is null ? null : names.GetValueOrDefault(i.DecidedBy.Value, "—"), decidedAt = i.DecidedAt,
                    productId = i.ProductId, productDeleted = i.ProductId != null && !alive.Contains(i.ProductId.Value),
                    info = BookQueries.ParseInfo(i.DataJson),
                }),
            });
        }

        /// <summary>核准／退回（可一次多本）。只能處理「待審」的；退回的書放回採購清單（清單裡已有同條碼就不重複放）</summary>
        [HttpPost("{id:int}/decide")]
        [RequirePermission("purchase.approve")]
        public async Task<IActionResult> Decide(int id, DecidePurchaseRequest req)
        {
            var o = await db.PurchaseOrders.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id && x.InstitutionId == InstitutionId);
            if (o is null) return NotFound(new { message = "找不到這張採購單" });
            if (!req.Approve && string.IsNullOrWhiteSpace(req.Reason)) return BadRequest(new { message = "退回要寫原因" });
            var items = o.Items.Where(i => req.ItemIds.Contains(i.Id) && i.Status == PurchaseItemStatus.Pending).ToList();
            if (items.Count == 0) return BadRequest(new { message = "沒有可以處理的（只能處理待審的）" });

            var now = DateTime.UtcNow;
            foreach (var i in items)
            {
                i.Status = req.Approve ? PurchaseItemStatus.Approved : PurchaseItemStatus.Rejected;
                i.RejectReason = req.Approve ? null : req.Reason!.Trim()[..Math.Min(req.Reason!.Trim().Length, 500)];
                i.DecidedBy = UserId;
                i.DecidedAt = now;
                if (!req.Approve && !await db.Books.AnyAsync(b => b.InstitutionId == InstitutionId && b.Code == i.Code))
                {
                    var back = $"（{o.OrderNo} 退回：{i.RejectReason}）";
                    db.Books.Add(new Book
                    {
                        InstitutionId = InstitutionId, Code = i.Code, Kind = i.Kind, Title = i.Title, Authors = i.Authors,
                        Publisher = i.Publisher, PubDate = i.PubDate, DataJson = i.DataJson,
                        Note = string.IsNullOrWhiteSpace(i.Note) ? back : $"{i.Note} {back}"[..Math.Min($"{i.Note} {back}".Length, 1000)],
                    });
                }
            }
            audit.Log(req.Approve ? "purchase.approve" : "purchase.reject", "purchase_order", o.Id, null,
                      new { o.OrderNo, items = items.Select(i => i.Code), req.Reason });
            await db.SaveChangesAsync();
            return Ok(new { count = items.Count });
        }

        /// <summary>核准的書「上架」成商品：填店內碼、名稱、分類、售價、庫存</summary>
        [HttpPost("items/{itemId:int}/list")]
        public async Task<IActionResult> ListItem(int itemId, ListPurchaseItemRequest req)
        {
            var item = await db.PurchaseOrderItems.Include(i => i.PurchaseOrder)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.PurchaseOrder!.InstitutionId == InstitutionId);
            if (item is null) return NotFound(new { message = "找不到這筆採購明細" });
            if (item.Status != PurchaseItemStatus.Approved) return BadRequest(new { message = "只有「核准」的才能上架" });
            var sku = req.Sku?.Trim() ?? "";
            if (sku == "") return BadRequest(new { message = "請輸入店內碼" });
            if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "請輸入商品名稱" });
            if (req.SalePrice is < 0 || req.Stock < 0) return BadRequest(new { message = "售價、庫存不能是負數" });
            if (await db.Products.AnyAsync(p => p.InstitutionId == InstitutionId && p.Sku == sku))
                return BadRequest(new { message = $"店內碼 {sku} 已經有商品在用了" });

            var info = BookQueries.ParseInfo(item.DataJson);
            var people = info is null ? [] : info.Kind == "product" ? (info.Brand is null ? [] : [info.Brand]) : info.Authors;
            var p = new Product
            {
                InstitutionId = InstitutionId, Sku = sku[..Math.Min(sku.Length, 30)], Name = req.Name.Trim()[..Math.Min(req.Name.Trim().Length, 500)],
                Code = item.Code, MidCategory = Blank(req.MidCategory), SubCategory = Blank(req.SubCategory),
                SalePrice = req.SalePrice, Stock = req.Stock, IsListed = req.IsListed,
                Authors = people.Count > 0 ? string.Join("、", people)[..Math.Min(string.Join("、", people).Length, 500)] : null,
                DataJson = item.DataJson,
            };
            db.Products.Add(p);
            await db.SaveChangesAsync();
            item.Status = PurchaseItemStatus.Listed;
            item.ProductId = p.Id;
            audit.Log("purchase.list", "product", p.Id, null, new { item.PurchaseOrder!.OrderNo, p.Sku, p.Name, p.Code });
            await db.SaveChangesAsync();
            return Ok(new { productId = p.Id });
        }

        /// <summary>採購單號：PO-yyyyMMdd-流水號（以台灣日期算）</summary>
        private async Task<string> NextOrderNoAsync(DateTime utcNow)
        {
            var prefix = $"PO-{utcNow.AddHours(8):yyyyMMdd}-";
            var last = await db.PurchaseOrders.Where(o => o.InstitutionId == InstitutionId && o.OrderNo.StartsWith(prefix))
                .OrderByDescending(o => o.OrderNo).Select(o => o.OrderNo).FirstOrDefaultAsync();
            var n = last is null ? 1 : int.Parse(last[prefix.Length..]) + 1;
            return $"{prefix}{n:000}";
        }

        private async Task<Dictionary<int, string>> UserNamesAsync(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
        }

        static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 100)];
    }
}
