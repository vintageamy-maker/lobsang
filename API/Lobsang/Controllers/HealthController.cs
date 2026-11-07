using Microsoft.AspNetCore.Mvc;
using Lobsang.Data;

namespace Lobsang.Controllers
{
    [ApiController]
    [Route("api/health")]
    public class HealthController : ControllerBase
    {
        private readonly AppDbContext _db;
        public HealthController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var dbOk = await _db.Database.CanConnectAsync();
            return Ok(new
            {
                status = "ok",
                db = dbOk ? "connected" : "unreachable",
                serverTime = DateTime.UtcNow,
                version = "0.1.0"
            });
        }
    }
}
