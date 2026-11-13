using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
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
            // STUDENTS USING EACH MODULE
            //
            // "Using" means the student has at least one record in that
            // module's own tables, so the number is driven by real activity
            // and not by who merely has an account.
            // -----------------------------------------
            var studentIds = await _context.Users
                .Where(u => u.Role == "Student" && u.AccountStatus != "Deleted")
                .Select(u => u.Id)
                .ToListAsync();

            var studentIdSet = studentIds.ToHashSet();

            // Library: attended, borrowed, reserved, or has a library activity.
            var libraryUserIds = new HashSet<int>();
            libraryUserIds.UnionWith(await _context.BorrowTransactions
                .Select(x => x.UserId).Distinct().ToListAsync());
            libraryUserIds.UnionWith(await _context.Reservations
                .Select(x => x.UserId).Distinct().ToListAsync());
            libraryUserIds.UnionWith(await _context.LibraryAttendances
                .Select(x => x.UserId).Distinct().ToListAsync());
            libraryUserIds.UnionWith(await _context.LibraryActivities
                .Select(x => x.UserId).Distinct().ToListAsync());

            // Lost & Found: filed a report or a claim.
            var lostFoundUserIds = new HashSet<int>();
            lostFoundUserIds.UnionWith(await _context.LostFoundReports
                .Select(x => x.UserId).Distinct().ToListAsync());
            lostFoundUserIds.UnionWith(await _context.ClaimRecords
                .Select(x => x.ClaimedByUserId).Distinct().ToListAsync());

            // Guidance: booked an appointment, had a counseling session, or
            // opened a chat with a counselor.
            var guidanceUserIds = new HashSet<int>();
            guidanceUserIds.UnionWith(await _context.GuidanceAppointments
                .Select(x => x.StudentId).Distinct().ToListAsync());
            guidanceUserIds.UnionWith(await _context.GuidanceCounselingRecords
                .Select(x => x.StudentId).Distinct().ToListAsync());
            guidanceUserIds.UnionWith(await _context.GuidanceChatConversations
                .Select(x => x.StudentId).Distinct().ToListAsync());

            // Clinic: the clinic itself is a separate CMS database. OneServe
            // records a marker the first time a student's clinic request
            // returns actual medical records, so that is what we count here.
            var clinicUserIds = await _context.UserActivities
                .Where(a => a.Action == UserActivityActions.ClinicRecordsViewed)
                .Select(a => a.UserId)
                .Distinct()
                .ToListAsync();

            var libraryStudents = libraryUserIds.Count(studentIdSet.Contains);
            var lostFoundStudents = lostFoundUserIds.Count(studentIdSet.Contains);
            var guidanceStudents = guidanceUserIds.Count(studentIdSet.Contains);
            var clinicStudents = clinicUserIds.Count(studentIdSet.Contains);

            // -----------------------------------------
            // MODULE STATUS
            // -----------------------------------------
            var modules = new[]
            {
                new
                {
                    name = "Lost & Found",
                    icon = "🔍",
                    status = "Online",
                    studentCount = (int?)lostFoundStudents,
                    usageNote = "students who filed a report or claim"
                },
                new
                {
                    name = "Library",
                    icon = "📚",
                    status = "Online",
                    studentCount = (int?)libraryStudents,
                    usageNote = "students who attended, borrowed or reserved"
                },
                new
                {
                    name = "Clinic",
                    icon = "🏥",
                    status = "Online",
                    studentCount = (int?)clinicStudents,
                    usageNote = "students with clinic medical records"
                },
                new
                {
                    name = "Guidance",
                    icon = "💬",
                    status = "Online",
                    studentCount = (int?)guidanceStudents,
                    usageNote = "students with an appointment, session or chat"
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