using System.Text.Json;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Library;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

public class LibraryAccessPassService
{
    private readonly AppDbContext _context;

    public LibraryAccessPassService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<LibraryAccessPassDto?> GetAccessPassAsync(int userId)
    {
        var user = await _context.Users
    .FirstOrDefaultAsync(u =>
        u.Id == userId &&
        (u.Role == "Student" || u.Role == "Faculty"));

        if (user == null)
            return null;

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var qrDataObject = new
        {
            userId = user.Id,
            idNumber = user.IdNumber,
            name = user.FullName,
            institute = user.Institute,
            program = user.Course,
            role = user.Role,
            timestamp = timestamp
        };

        var qrData = JsonSerializer.Serialize(qrDataObject);

        return new LibraryAccessPassDto
        {
            UserId = user.Id,
            IdNumber = user.IdNumber,
            Name = user.FullName,
            Institute = user.Institute,
            Program = user.Course,
            Role = user.Role,
            Timestamp = timestamp,
            QrData = qrData
        };
    }
}