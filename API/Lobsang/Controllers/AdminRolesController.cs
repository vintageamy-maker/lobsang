using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Lobsang.Auth;
using Lobsang.Data;
using Lobsang.Models;
using Lobsang.Services;
using System.Security.Claims;

namespace Lobsang.Controllers
{
    public record SaveRoleRequest(string Name, string? Description, List<int> PermissionIds);

    [ApiController]
    [Route("api/admin/roles")]
    [RequirePermission("role.manage")]
    public class AdminRolesController(AppDbContext db, AuditLogger audit) : ControllerBase
    {
        private int InstitutionId => int.Parse(User.FindFirstValue("inst")!);

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var roles = await db.Roles
                .Include(r => r.RolePermissions)
                .Where(r => r.InstitutionId == InstitutionId)
                .OrderBy(r => r.Id)
                .ToListAsync();

            var userCounts = await db.UserRoles
                .Where(ur => roles.Select(r => r.Id).Contains(ur.RoleId))
                .GroupBy(ur => ur.RoleId)
                .Select(g => new { RoleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RoleId, x => x.Count);

            return Ok(roles.Select(r => new
            {
                id = r.Id,
                name = r.Name,
                // ⭐ 標準角色的固定代碼（2026-10-09）：改名不影響程式判斷；院方自建的角色是 null
                code = r.Code,
                description = r.Description,
                permissionIds = r.RolePermissions.Select(rp => rp.PermissionId),
                userCount = userCounts.GetValueOrDefault(r.Id),
                createdAt = r.CreatedAt,
            }));
        }

        /// <summary>
        /// 這個角色掛在誰身上。
        ///
        /// 清單頁只給得出一個數字，而「10 個人有這個權限」跟「哪 10 個人有」
        /// 是兩件事 —— 要判斷一個角色改動的影響範圍，看的是後者。
        ///
        /// ⚠ 停用中的人也列出並標示。把他們濾掉會讓數字對不上清單頁，
        /// 而且復職時權限會原封不動回來，那正是需要在調整角色前看到的事。
        /// </summary>
        [HttpGet("{id:int}/users")]
        public async Task<IActionResult> Users(int id)
        {
            var role = await db.Roles.FirstOrDefaultAsync(
                r => r.Id == id && r.InstitutionId == InstitutionId);
            if (role is null) return NotFound(new { message = "找不到角色", code = "role_not_found" });

            var rows = await db.UserRoles
                .Where(ur => ur.RoleId == id)
                .Join(db.Users.Where(u => u.InstitutionId == InstitutionId),
                    ur => ur.UserId, u => u.Id, (_, u) => u)
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    u.Account,
                    u.Title,
                    orgUnitName = u.OrgUnit!.Name,
                    u.IsActive,
                    u.SortOrder, u.EmployeeNo,
                    // 兼掛的其他角色：判斷「拿掉這個角色他還剩什麼」要靠它
                    otherRoles = u.UserRoles.Where(x => x.RoleId != id)
                        .Select(x => x.Role!.Name).ToList(),
                })
                .ToListAsync();

            return Ok(new
            {
                roleId = role.Id,
                roleName = role.Name,
                total = rows.Count,
                users = rows.OrderByDescending(u => u.IsActive).ThenBy(u => u.SortOrder).ThenBy(u => u.EmployeeNo).ThenBy(u => u.Id),
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create(SaveRoleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "角色名稱為必填", code = "required" });
            if (await db.Roles.AnyAsync(r => r.InstitutionId == InstitutionId && r.Name == request.Name))
                return BadRequest(new { message = $"角色「{request.Name}」已存在", code = "duplicate_role" });

            var role = new Role { InstitutionId = InstitutionId, Name = request.Name.Trim(), Description = request.Description };
            db.Roles.Add(role);
            await db.SaveChangesAsync();

            db.RolePermissions.AddRange(request.PermissionIds.Distinct().Select(pid => new RolePermission { RoleId = role.Id, PermissionId = pid }));
            audit.Log("role.create", "role", role.Id, null, new { role.Name, role.Description, permissionIds = request.PermissionIds });
            await db.SaveChangesAsync();

            return CreatedAtAction(nameof(List), null, new { id = role.Id });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, SaveRoleRequest request)
        {
            var role = await db.Roles.Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Id == id && r.InstitutionId == InstitutionId);
            if (role is null) return NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest(new { message = "角色名稱為必填", code = "required" });
            if (await db.Roles.AnyAsync(r => r.InstitutionId == InstitutionId && r.Id != id && r.Name == request.Name.Trim()))
                return BadRequest(new { message = $"角色「{request.Name}」已存在", code = "duplicate_role" });

            var before = new { role.Name, role.Description, permissionIds = role.RolePermissions.Select(rp => rp.PermissionId).ToList() };

            role.Name = request.Name.Trim();
            role.Description = request.Description;
            db.RolePermissions.RemoveRange(role.RolePermissions);
            db.RolePermissions.AddRange(request.PermissionIds.Distinct().Select(pid => new RolePermission { RoleId = role.Id, PermissionId = pid }));

            audit.Log("role.update", "role", role.Id, before, new { request.Name, request.Description, permissionIds = request.PermissionIds });
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id && r.InstitutionId == InstitutionId);
            if (role is null) return NotFound();

            // ⚠ 系統管理員是唯一保證有全部權限的角色，刪掉就沒有人能管理系統
            if (role.Code == RoleCodes.SysAdmin)
                return BadRequest(new { message = "系統管理員角色不能刪除（可以改名）", code = "role_protected" });

            var inUse = await db.UserRoles.CountAsync(ur => ur.RoleId == id);
            if (inUse > 0)
                return BadRequest(new { message = $"仍有 {inUse} 位使用者屬於此角色，請先調整後再刪除", code = "role_in_use" });

            audit.Log("role.delete", "role", role.Id, new { role.Name, role.Code }, null);
            db.Roles.Remove(role);
            await db.SaveChangesAsync();
            return NoContent();
        }
    }

    [ApiController]
    [Route("api/admin/permissions")]
    public class AdminPermissionsController(AppDbContext db) : ControllerBase
    {
        /// <summary>權限字典（供角色管理勾選）：user.manage 或 role.manage 皆可讀</summary>
        [HttpGet]
        [RequirePermission("role.manage")]
        public async Task<IActionResult> List()
        {
            var permissions = await db.Permissions.OrderBy(p => p.Id).ToListAsync();
            // isDelegable 一併回傳：這是**代理人能否繼承**的依據。
            // 不回傳的話，權限設定畫面看不出哪些權限不會被代理，
            // 等於這條規則只活在資料庫裡，設定的人不知道它存在。
            return Ok(permissions.Select(p => new
            {
                id = p.Id, code = p.Code, name = p.Name, group = p.Group, isDelegable = p.IsDelegable,
            }));
        }
    }
}
