using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

// =========================================================
// SCHOOL RECORDS (admin)
// The official ID registry: only IDs in this table may
// register. Served to the admin School Records page.
//
//   GET    /api/admin/school-records
//   POST   /api/admin/school-records        { idNumber, role }
//   DELETE /api/admin/school-records/{id}
// =========================================================

[ApiController]
[Route("api/admin/school-records")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class SchoolRecordsController : ControllerBase
{
    private readonly AppDbContext _context;

    public SchoolRecordsController(AppDbContext context)
    {
        _context = context;
    }

    private static string NormalizeIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
            return "";

        return idNumber
            .Trim()
            .Replace(" ", "")
            .ToUpperInvariant();
    }

    // -----------------------------------------------------
    // LIST
    // -----------------------------------------------------

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var records = await _context.SchoolRecords
            .AsNoTracking()
            .OrderBy(x => x.IdNumber)
            .Select(x => new
            {
                x.Id,
                x.IdNumber,
                x.Role,
                x.IsUsed,
                x.CreatedAt
            })
            .ToListAsync();

        return Ok(records);
    }

    // -----------------------------------------------------
    // ADD
    // -----------------------------------------------------

    public class AddSchoolRecordRequest
    {
        public string IdNumber { get; set; } = "";
        public string Role { get; set; } = "Student";
    }

    [HttpPost]
    public async Task<IActionResult> Add(
        [FromBody] AddSchoolRecordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.IdNumber))
            return BadRequest(new { message = "ID Number is required." });

        var role = request.Role?.Trim();

        if (role != "Student" && role != "Faculty")
        {
            return BadRequest(new
            {
                message = "Role must be Student or Faculty."
            });
        }

        var normalized = NormalizeIdNumber(request.IdNumber);

        var exists = await _context.SchoolRecords
            .AnyAsync(x =>
                x.IdNumber.Replace(" ", "").ToUpper() == normalized);

        if (exists)
        {
            return BadRequest(new
            {
                message = "This ID Number is already in the school records."
            });
        }

        var record = new SchoolRecord
        {
            IdNumber = request.IdNumber.Trim(),
            Role = role,
            IsUsed = false
        };

        _context.SchoolRecords.Add(record);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "School record added.",
            record = new
            {
                record.Id,
                record.IdNumber,
                record.Role,
                record.IsUsed
            }
        });
    }

    // -----------------------------------------------------
    // DELETE
    // -----------------------------------------------------

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var record = await _context.SchoolRecords
            .FirstOrDefaultAsync(x => x.Id == id);

        if (record == null)
        {
            return NotFound(new { message = "School record not found." });
        }

        if (record.IsUsed)
        {
            return BadRequest(new
            {
                message = "This ID already registered an account and cannot be removed."
            });
        }

        _context.SchoolRecords.Remove(record);
        await _context.SaveChangesAsync();

        return Ok(new { message = "School record removed." });
    }
}
