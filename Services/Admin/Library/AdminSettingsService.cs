using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Settings;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Library;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminSettingsService : IAdminSettingsService
{
    private readonly AppDbContext _db;
    private readonly IAdminLibraryActivityLogger _activity;

    public AdminSettingsService(AppDbContext db, IAdminLibraryActivityLogger activity)
    {
        _db = db;
        _activity = activity;
    }

    // Called once at startup (Program.cs) so the snapshot matches the database.
    public static async Task ApplyFromDatabaseAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var row = await db.LibrarySettingsRecords.AsNoTracking()
            .OrderBy(s => s.SettingId)
            .FirstOrDefaultAsync(cancellationToken);

        if (row != null) Apply(row);
    }

    private static void Apply(LibrarySettings s) =>
        LibraryDefaults.Apply(s.OverdueFinePerDay, s.StudentBorrowLimit, s.FacultyBorrowLimit,
            s.DefaultLoanDays, s.ReservationPickupDays);

    public async Task<LibrarySettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var row = await _db.LibrarySettingsRecords.AsNoTracking()
            .OrderBy(s => s.SettingId)
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null)
        {
            // No row yet: show what the system is using right now.
            return new LibrarySettingsDto
            {
                OverdueFinePerDay = LibraryDefaults.OverdueFinePerDay,
                StudentBorrowLimit = LibraryDefaults.StudentBorrowLimit,
                FacultyBorrowLimit = LibraryDefaults.FacultyBorrowLimit,
                DefaultLoanDays = LibraryDefaults.DefaultLoanDays,
                ReservationPickupDays = LibraryDefaults.ReservationPickupDays,
                LoanDayOptions = LibraryDefaults.LoanDayOptions
            };
        }

        // Keep the live values in step with the database (covers manual edits in phpMyAdmin).
        Apply(row);

        string? name = null;

        if (row.UpdatedBy != null)
            name = await _db.Users.AsNoTracking()
                .Where(u => u.Id == row.UpdatedBy)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(cancellationToken);

        return ToDto(row, name);
    }

    public async Task<LibraryResult<LibrarySettingsDto>> UpdateAsync(
        UpdateLibrarySettingsDto dto, int headId, CancellationToken cancellationToken)
    {
        var error = Validate(dto);
        if (error != null) return LibraryResult<LibrarySettingsDto>.Failure(400, error);

        var row = await _db.LibrarySettingsRecords.OrderBy(s => s.SettingId).FirstOrDefaultAsync(cancellationToken);

        if (row == null)
        {
            row = new LibrarySettings();
            _db.LibrarySettingsRecords.Add(row);
        }

        var before = $"fine {row.OverdueFinePerDay:0.00}, limits {row.StudentBorrowLimit}/{row.FacultyBorrowLimit}, " +
                     $"loan {row.DefaultLoanDays}d, pickup {row.ReservationPickupDays}d";

        row.OverdueFinePerDay = Math.Round(dto.OverdueFinePerDay, 2);
        row.StudentBorrowLimit = dto.StudentBorrowLimit;
        row.FacultyBorrowLimit = dto.FacultyBorrowLimit;
        row.DefaultLoanDays = dto.DefaultLoanDays;
        row.ReservationPickupDays = dto.ReservationPickupDays;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = headId;

        var after = $"fine {row.OverdueFinePerDay:0.00}, limits {row.StudentBorrowLimit}/{row.FacultyBorrowLimit}, " +
                    $"loan {row.DefaultLoanDays}d, pickup {row.ReservationPickupDays}d";

        _activity.Add(headId, "LIBRARY_SETTINGS_UPDATED", $"changed the library settings ({before} to {after})");

        await _db.SaveChangesAsync(cancellationToken);

        // Only after the database accepted the change.
        Apply(row);

        return LibraryResult<LibrarySettingsDto>.Success(await GetAsync(cancellationToken));
    }

    private static string? Validate(UpdateLibrarySettingsDto d)
    {
        if (d.OverdueFinePerDay < 0 || d.OverdueFinePerDay > 1000)
            return "The overdue fine per day must be between 0 and 1,000.";

        if (d.StudentBorrowLimit < 1 || d.StudentBorrowLimit > 50)
            return "The student borrowing limit must be between 1 and 50.";

        if (d.FacultyBorrowLimit < 1 || d.FacultyBorrowLimit > 50)
            return "The faculty borrowing limit must be between 1 and 50.";

        if (!LibraryDefaults.LoanDayOptions.Contains(d.DefaultLoanDays))
            return $"The default loan period must be one of: {string.Join(", ", LibraryDefaults.LoanDayOptions)} days.";

        if (d.ReservationPickupDays < 1 || d.ReservationPickupDays > 14)
            return "The reservation pickup period must be between 1 and 14 days.";

        return null;
    }

    private static LibrarySettingsDto ToDto(LibrarySettings s, string? updatedByName) => new()
    {
        OverdueFinePerDay = s.OverdueFinePerDay,
        StudentBorrowLimit = s.StudentBorrowLimit,
        FacultyBorrowLimit = s.FacultyBorrowLimit,
        DefaultLoanDays = s.DefaultLoanDays,
        ReservationPickupDays = s.ReservationPickupDays,
        UpdatedAt = s.UpdatedAt,
        UpdatedByName = updatedByName,
        LoanDayOptions = LibraryDefaults.LoanDayOptions
    };
}