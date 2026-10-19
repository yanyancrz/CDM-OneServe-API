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
            var totalStudents = await usersQuery
                .Where(u => u.Role == "Student" && u.AccountStatus != "Deleted").CountAsync();
            var totalFaculty = await usersQuery
                .Where(u => u.Role == "Faculty" && u.AccountStatus != "Deleted").CountAsync();

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
                totalStudents,
                totalFaculty,
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