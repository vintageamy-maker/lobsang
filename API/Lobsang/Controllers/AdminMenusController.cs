using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;

namespace Lobsang.Controllers
{
    public record SaveMenuRequest(string Name, int? ParentId, string? Path, string? Icon, int SortOrder, string? PermissionCode, bool IsActive);
    public record ReorderMenusRequest(int? ParentId, List<int> Ids);

    public record MenuNode(int Id, string Name, string? Path, string? Icon, int SortOrder, string? PermissionCode, bool IsActive, List<MenuNode> Children);

    [ApiController]
    [Route("api/admin/menus")]
    [RequirePermission("menu.manage")]
    public class AdminMenusController(AppDbContext db, AuditLogger audit) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Tree()
        {
            var menus = await db.Menus.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();
            return Ok(BuildTree(menus, null));
        }

        [HttpPost]
        public async Task<IActionResult> Create(SaveMenuRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "選單名稱為必填", code = "required" });

            // SortOrder 未指定（<=0）時自動接在同層最後（排序以拖曳維護）
            var sortOrder = request.SortOrder > 0
                ? request.SortOrder
                : (await db.Menus.Where(m => m.ParentId == request.ParentId).MaxAsync(m => (int?)m.SortOrder) ?? 0) + 1;

            var menu = new Menu
            {
                Name = request.Name.Trim(),
                ParentId = request.ParentId,
                Path = request.Path,
                Icon = request.Icon,
                SortOrder = sortOrder,
                PermissionCode = request.PermissionCode,
                IsActive = request.IsActive,
            };
            db.Menus.Add(menu);
            audit.Log("menu.create", "menu", null, null, new { request.Name, request.Path, request.PermissionCode });
            await db.SaveChangesAsync();
            return Ok(new { id = menu.Id });
        }

        /// <summary>同層兄弟群重排：ids 為該層新順序（需與現況成員一致）</summary>
        [HttpPut("reorder")]
        public async Task<IActionResult> Reorder(ReorderMenusRequest request)
        {
            var siblings = await db.Menus.Where(m => m.ParentId == request.ParentId).ToListAsync();
            var byId = siblings.ToDictionary(m => m.Id);
            if (request.Ids.Count != siblings.Count || request.Ids.Any(id => !byId.ContainsKey(id)))
                return BadRequest(new { message = "排序清單與現況不一致，請重新整理後再試", code = "reorder_mismatch" });

            for (var i = 0; i < request.Ids.Count; i++)
                byId[request.Ids[i]].SortOrder = i + 1;

            audit.Log("menu.reorder", "menu", request.ParentId, null, new { request.ParentId, request.Ids });
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, SaveMenuRequest request)
        {
            var menu = await db.Menus.FindAsync(id);
            if (menu is null) return NotFound();
            if (request.ParentId == id)
                return BadRequest(new { message = "上級選單不可為自己", code = "invalid_parent" });

            var before = new { menu.Name, menu.ParentId, menu.Path, menu.Icon, menu.SortOrder, menu.PermissionCode, menu.IsActive };

            menu.Name = request.Name.Trim();
            menu.ParentId = request.ParentId;
            menu.Path = request.Path;
            menu.Icon = request.Icon;
            menu.SortOrder = request.SortOrder;
            menu.PermissionCode = request.PermissionCode;
            menu.IsActive = request.IsActive;

            audit.Log("menu.update", "menu", id, before, request);
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var menu = await db.Menus.FindAsync(id);
            if (menu is null) return NotFound();

            var childCount = await db.Menus.CountAsync(m => m.ParentId == id);
            if (childCount > 0)
                return BadRequest(new { message = $"此選單尚有 {childCount} 個子選單，請先移除", code = "menu_has_children" });

            audit.Log("menu.delete", "menu", id, new { menu.Name, menu.Path }, null);
            db.Menus.Remove(menu);
            await db.SaveChangesAsync();
            return NoContent();
        }

        internal static List<MenuNode> BuildTree(List<Menu> menus, int? parentId) =>
            menus.Where(m => m.ParentId == parentId)
                .Select(m => new MenuNode(m.Id, m.Name, m.Path, m.Icon, m.SortOrder, m.PermissionCode, m.IsActive,
                    BuildTree(menus, m.Id)))
                .ToList();
    }
}
