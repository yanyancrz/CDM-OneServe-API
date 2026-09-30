using CDM_OneServe_API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/dashboard")]
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
            // -----------------------------------------
            var recentActivity = await usersQuery
                .Where(u =>
                    u.PhysicalIdVerificationStatus == "Verified" ||
                    u.PhysicalIdVerificationStatus == "Rejected" ||
                    u.PhysicalIdVerificationStatus == "Re-upload Required")
                .OrderByDescending(u => u.PhysicalIdVerifiedAt ?? u.CreatedAt)
                .Take(5)
                .Select(u => new
                {
                    id = u.Id,

                    title =
                        u.PhysicalIdVerificationStatus == "Verified"
                            ? "Physical ID verified"
                            : u.PhysicalIdVerificationStatus == "Rejected"
                                ? "Physical ID rejected"
                                : "Physical ID re-upload requested",

                    description =
                        $"{u.FullName} ({u.IdNumber})",

                    createdAt =
                        u.PhysicalIdVerifiedAt ?? u.CreatedAt
                })
                .ToListAsync();

            // -----------------------------------------
            // MODULE STATUS
            // -----------------------------------------
            var modules = new[]
            {
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
                },
                new
                {
                    name = "Lost & Found",
                    icon = "🔎",
                    status = "Online"
                },
                new
                {
                    name = "Business Hub",
                    icon = "🏪",
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