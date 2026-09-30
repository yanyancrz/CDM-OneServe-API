using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

public class LibraryAttendanceService
{
    private readonly AppDbContext _context;

    public LibraryAttendanceService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<object> RecordAttendanceAsync(
        int userId,
        string studentNumber,
        string studentName,
        string? institute,
        string? purpose)
    {
        var attendance = new LibraryAttendance
        {
            UserId = userId,
            StudentNumber = studentNumber,
            StudentName = studentName,
            Institute = institute,
            Purpose = purpose,
            Timestamp = DateTime.Now,
            CreatedAt = DateTime.Now
        };

        _context.LibraryAttendances.Add(attendance);

        await _context.SaveChangesAsync();

        return new
        {
            success = true,
            message = "Attendance recorded successfully.",
            attendanceId = attendance.AttendanceId,
            timestamp = attendance.Timestamp
        };
    }

    public async Task<List<LibraryAttendance>> GetStudentAttendanceAsync(
        int userId)
    {
        return await _context.LibraryAttendances
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Timestamp)
            .ToListAsync();
    }
}