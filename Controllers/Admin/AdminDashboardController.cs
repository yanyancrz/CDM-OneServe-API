using CDM_OneServe_API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminDashboardController : ControllerBase
{
    private readonly AppDbContext _context;

    public AdminDashboardController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/admin/dashboard
    [HttpGet]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            // Only Student and Faculty accounts
            var usersQuery = _context.Users
                .Where(u =>
                    u.Role == "Student" ||
                    u.Role == "Faculty");

            // -----------------------------------------
            // TOTAL USERS
            // -----------------------------------------
            var totalUsers = await usersQuery.CountAsync();

            // -----------------------------------------
            // PENDING PHYSICAL ID
            // -----------------------------------------
            var pendingPhysicalId = await usersQuery.CountAsync(u =>
                u.PhysicalIdVerificationStatus == "Pending" ||
                u.PhysicalIdVerificationStatus == "Submitted" ||
                u.AccountStatus == "Pending"
            );

            // -----------------------------------------
            // VERIFIED PHYSICAL ID
            // -----------------------------------------
            var verifiedPhysicalId = await usersQuery.CountAsync(u =>
                u.PhysicalIdVerificationStatus == "Verified"
            );

            // -----------------------------------------
            // REJECTED PHYSICAL ID
            // -----------------------------------------
            var rejectedPhysicalId = await usersQuery.CountAsync(u =>
                u.PhysicalIdVerificationStatus == "Rejected"
            );

            // -----------------------------------------
            // RECENT ACTIVITY
            // Lahat ng ginawa ng lahat ng admin (AdminActivityLogs),
            // pinakabago muna.
            // -----------------------------------------
            var recentActivity = await (
                from log in _context.AdminActivityLogs
                where !log.Action.StartsWith("LIBRARY_")
                join admin in _context.Users
                    on log.UserId equals admin.Id into adminGroup
                from admin in adminGroup.DefaultIfEmpty()
                orderby log.Timestamp descending
                select new
                {
                    id = log.Id,
                    title = log.Action,
                    description = admin != null
                        ? "By " + admin.FullName
                        : "By Admin",
                    createdAt = log.Timestamp
                })
                .Take(20)
                .ToListAsync();

            // -----------------------------------------
            // MODULE STATUS
            // -----------------------------------------
            var modules = new[]
            {
                new
                {
                    name = "Lost & Found",
                    icon = "🔍",
                    status = "Online"
                },
                new
                {
                    name = "Library",
                    icon = "📚",
                    status = "Online"
                },
                new
                {
                    name = "Clinic",
                    icon = "🏥",
                    status = "Online"
                },
                new
                {
                    name = "Guidance",
                    icon = "💬",
                    status = "Online"
                }
            };

            // -----------------------------------------
            // RESPONSE
            // -----------------------------------------
            return Ok(new
            {
                totalUsers,
                pendingPhysicalId,
                verifiedPhysicalId,
                rejectedPhysicalId,
                recentActivity,
                modules
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Admin dashboard error: {ex.Message}"
            );

            return StatusCode(
                500,
                new
                {
                    message = "Failed to load admin dashboard."
                }
            );
        }
    }
}