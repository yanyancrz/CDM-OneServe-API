using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

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
        _db.AdminActivityLogs.Add(new AdminActivityLog
        {
            UserId = userId,
            Action = $"{code}: {message}",
            Timestamp = DateTime.Now
        });
    }
}