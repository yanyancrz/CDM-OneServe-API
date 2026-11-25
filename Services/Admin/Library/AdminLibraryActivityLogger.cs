using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public interface IAdminLibraryActivityLogger
{
    // Adds the log but does NOT save, so it joins the caller's transaction.
    void Add(int userId, string code, string message);
}

public class AdminLibraryActivityLogger : IAdminLibraryActivityLogger
{
    private readonly AppDbContext _db;

    public AdminLibraryActivityLogger(AppDbContext db) => _db = db;

    public void Add(int userId, string code, string message)
    {
        // Central audit fields. Without Module/ActorName these rows were
        // invisible on the Library tab and showed a raw user id instead of
        // a name, so a Head's work read like an anonymous entry. The actor
        // lookup is a primary-key read on the same context - no save here,
        // the row still joins the caller's transaction.
        var actor = _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Role })
            .FirstOrDefault();

        _db.AdminActivityLogs.Add(new AdminActivityLog
        {
            UserId = userId,
            Action = $"{code}: {message}",
            Timestamp = DateTime.UtcNow.AddHours(8), // PH wall clock, like AdminAuditService
            Module = "Library",
            Category = "Library",
            ActorName = actor?.FullName,
            ActorRole = actor?.Role
        });
    }
}
