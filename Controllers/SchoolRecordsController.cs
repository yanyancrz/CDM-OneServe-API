using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/admin/school-records")]
public class SchoolRecordsController : ControllerBase
{
    private readonly AppDbContext _context;

    private const long MaxFileSize =
        10 * 1024 * 1024;

    public SchoolRecordsController(
        AppDbContext context)
    {
        _context = context;
    }

    // =========================================================
    // HELPER — AUTOMATIC INSTITUTE FROM COURSE
    // =========================================================

    private static string? GetInstituteFromCourse(
        string? course)
    {
        if (string.IsNullOrWhiteSpace(course))
            return null;

        var normalizedCourse =
            course.Trim();

        return normalizedCourse switch
        {
            // =================================================
            // INSTITUTE OF COMPUTING STUDIES
            // =================================================

            "Bachelor of Science in Computer Engineering"
                => "Institute of Computing Studies",

            "Bachelor of Science in Information Technology"
                => "Institute of Computing Studies",

            "BS Information Technology"
                => "Institute of Computing Studies",

            "BS IT"
                => "Institute of Computing Studies",

            // =================================================
            // INSTITUTE OF TEACHER EDUCATION
            // =================================================

            "Bachelor of Early Childhood Education"
                => "Institute of Teacher Education",

            "Bachelor of Technology and Livelihood Education Major in Information and Communication Technology"
                => "Institute of Teacher Education",

            "Bachelor of Science in Secondary Education Major in Science"
                => "Institute of Teacher Education",

            "Bachelor of Elementary Education Major in General Education"
                => "Institute of Teacher Education",

            "Teacher Certificate Program"
                => "Institute of Teacher Education",

            // =================================================
            // INSTITUTE OF BUSINESS AND ENTREPRENEURSHIP
            // =================================================

            "Bachelor of Science in Business Administration Major in Human Resource Management"
                => "Institute of Business and Entrepreneurship",

            "Bachelor of Science in Entrepreneurship"
                => "Institute of Business and Entrepreneurship",

            _ => null
        };
    }

    // =========================================================
    // TEST
    // =========================================================

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(
            "School Records Controller is working"
        );
    }

    // =========================================================
    // GET STUDENTS
    // =========================================================

    [HttpGet("students")]
    public async Task<IActionResult> GetStudents()
    {
        // Load official student records first.
        // Institute is calculated from Course as a fallback so
        // existing records with a null Institute still display correctly.
        var records = await _context.StudentRecords
            .OrderBy(x => x.FullName)
            .Select(x => new
            {
                x.Id,
                x.StudentIdNumber,
                x.FullName,
                x.Email,
                x.Course,
                x.Institute,
                x.YearLevel,
                x.EnrollmentStatus,
                x.AcademicYear,
                x.Semester,
                x.ImportBatchId,
                x.CreatedAt,
                x.UpdatedAt
            })
            .ToListAsync();

        // Get system accounts separately.
        // StudentRecord.StudentIdNumber <-> User.IdNumber
        var users = await _context.Users
            .Select(x => new
            {
                x.Id,
                x.IdNumber,
                x.Role,
                x.AccountStatus
            })
            .ToListAsync();

        var usersByIdNumber = users
            .Where(x => !string.IsNullOrWhiteSpace(x.IdNumber))
            .GroupBy(
                x => x.IdNumber!.Trim(),
                StringComparer.OrdinalIgnoreCase
            )
            .ToDictionary(
                x => x.Key,
                x => x.First(),
                StringComparer.OrdinalIgnoreCase
            );

        var students = records.Select(student =>
        {
            usersByIdNumber.TryGetValue(
                student.StudentIdNumber?.Trim() ?? "",
                out var account
            );

            // Prefer the official saved Institute.
            // If it is missing, derive it from the Course.
            var institute =
                !string.IsNullOrWhiteSpace(student.Institute)
                    ? student.Institute
                    : GetInstituteFromCourse(student.Course);

            return new
            {
                student.Id,
                student.StudentIdNumber,
                student.FullName,
                student.Email,
                student.Course,
                Institute = institute,
                student.YearLevel,
                student.EnrollmentStatus,
                student.AcademicYear,
                student.Semester,
                student.ImportBatchId,
                student.CreatedAt,
                student.UpdatedAt,

                HasSystemAccount = account != null,

                SystemAccountStatus =
                    account == null
                        ? "No Account"
                        : account.AccountStatus ?? "Active",

                SystemAccountUserId =
                    account?.Id,

                SystemAccountRole =
                    account?.Role
            };
        }).ToList();

        return Ok(students);
    }

    // =========================================================
    // GET FACULTY
    // =========================================================

    [HttpGet("faculty")]
    public async Task<IActionResult> GetFaculty()
    {
        var records = await _context.FacultyRecords
            .OrderBy(x => x.FullName)
            .Select(x => new
            {
                x.Id,
                x.FacultyIdNumber,
                x.FullName,
                x.Email,
                x.Institute,
                x.Position,
                x.EmploymentStatus,
                x.AcademicYear,
                x.ImportBatchId,
                x.CreatedAt,
                x.UpdatedAt
            })
            .ToListAsync();

        var users = await _context.Users
            .Select(x => new
            {
                x.Id,
                x.IdNumber,
                x.Role,
                x.AccountStatus
            })
            .ToListAsync();

        var usersByIdNumber = users
            .Where(x => !string.IsNullOrWhiteSpace(x.IdNumber))
            .GroupBy(
                x => x.IdNumber!.Trim(),
                StringComparer.OrdinalIgnoreCase
            )
            .ToDictionary(
                x => x.Key,
                x => x.First(),
                StringComparer.OrdinalIgnoreCase
            );

        var faculty = records.Select(member =>
        {
            usersByIdNumber.TryGetValue(
                member.FacultyIdNumber?.Trim() ?? "",
                out var account
            );

            return new
            {
                member.Id,
                member.FacultyIdNumber,
                member.FullName,
                member.Email,
                member.Institute,
                member.Position,
                member.EmploymentStatus,
                member.AcademicYear,
                member.ImportBatchId,
                member.CreatedAt,
                member.UpdatedAt,

                HasSystemAccount = account != null,

                SystemAccountStatus =
                    account == null
                        ? "No Account"
                        : account.AccountStatus ?? "Active",

                SystemAccountUserId =
                    account?.Id,

                SystemAccountRole =
                    account?.Role
            };
        }).ToList();

        return Ok(faculty);
    }

    // =========================================================
    // GET IMPORT HISTORY
    // =========================================================

    [HttpGet("imports")]
    public async Task<IActionResult> GetImportHistory()
    {
        var imports =
            await _context.SchoolRecordImports
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id,

                    x.BatchId,

                    x.SourceType,

                    x.FileName,

                    x.RecordType,

                    x.AcademicYear,

                    x.Semester,

                    x.TotalRecords,

                    x.ImportedBy,

                    x.CreatedAt
                })
                .ToListAsync();

        return Ok(imports);
    }

    // =========================================================
    // GET STUDENT BY ID
    // =========================================================

    [HttpGet("students/{id}")]
    public async Task<IActionResult> GetStudent(
        int id)
    {
        var student =
            await _context.StudentRecords
                .Where(x => x.Id == id)
                .FirstOrDefaultAsync();

        if (student == null)
        {
            return NotFound(new
            {
                message =
                    "Student record not found."
            });
        }

        return Ok(new
        {
            student.Id,

            student.StudentIdNumber,

            student.FullName,

            student.Email,

            student.Course,

            // AUTOMATIC INSTITUTE
            Institute =
                GetInstituteFromCourse(
                    student.Course)
                ?? student.Institute,

            student.YearLevel,

            student.EnrollmentStatus,

            student.AcademicYear,

            student.Semester,

            student.ImportBatchId,

            student.CreatedAt,

            student.UpdatedAt
        });
    }

    // =========================================================
    // GET FACULTY BY ID
    // =========================================================

    [HttpGet("faculty/{id}")]
    public async Task<IActionResult> GetFacultyMember(
        int id)
    {
        var faculty =
            await _context.FacultyRecords
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,

                    x.FacultyIdNumber,

                    x.FullName,

                    x.Email,

                    x.Institute,

                    x.Position,

                    x.EmploymentStatus,

                    x.AcademicYear,

                    x.ImportBatchId,

                    x.CreatedAt,

                    x.UpdatedAt
                })
                .FirstOrDefaultAsync();

        if (faculty == null)
        {
            return NotFound(new
            {
                message =
                    "Faculty record not found."
            });
        }

        return Ok(faculty);
    }

    // =========================================================
    // IMPORT STUDENTS FROM EXCEL
    // =========================================================

    [HttpPost("students/import")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> ImportStudents(
        IFormFile file,
        [FromQuery] int adminId,
        [FromQuery] string? academicYear = null,
        [FromQuery] string? semester = null)
    {
        if (file == null ||
            file.Length == 0)
        {
            return BadRequest(new
            {
                message =
                    "Please select an Excel file."
            });
        }

        if (file.Length > MaxFileSize)
        {
            return BadRequest(new
            {
                message =
                    "File must be 10 MB or smaller."
            });
        }

        var extension =
            Path.GetExtension(
                file.FileName)
                .ToLowerInvariant();

        if (extension != ".xlsx")
        {
            return BadRequest(new
            {
                message =
                    "Only .xlsx Excel files are supported."
            });
        }

        try
        {
            using var stream =
                file.OpenReadStream();

            using var workbook =
                new XLWorkbook(stream);

            var worksheet =
                workbook.Worksheets
                    .FirstOrDefault();

            if (worksheet == null)
            {
                return BadRequest(new
                {
                    message =
                        "The Excel file contains no worksheet."
                });
            }

            var headerMap =
                BuildHeaderMap(
                    worksheet);

            string[] requiredHeaders =
            {
                "StudentIdNumber",
                "FullName"
            };

            var missingHeaders =
                requiredHeaders
                    .Where(x =>
                        !headerMap.ContainsKey(x))
                    .ToList();

            if (missingHeaders.Count > 0)
            {
                return BadRequest(new
                {
                    message =
                        "Missing required columns.",

                    missingColumns =
                        missingHeaders
                });
            }

            var batchId =
                $"STUDENT-{DateTime.Now:yyyyMMddHHmmssfff}";

            var validRecords =
                new List<StudentRecord>();

            var duplicateRows =
                new List<int>();

            var invalidRows =
                new List<int>();

            var lastRow =
                worksheet.LastRowUsed()
                    ?.RowNumber() ?? 1;

            for (
                int row = 2;
                row <= lastRow;
                row++)
            {
                var studentId =
                    GetCellValue(
                        worksheet,
                        row,
                        headerMap,
                        "StudentIdNumber");

                var fullName =
                    GetCellValue(
                        worksheet,
                        row,
                        headerMap,
                        "FullName");

                if (
                    string.IsNullOrWhiteSpace(
                        studentId) ||
                    string.IsNullOrWhiteSpace(
                        fullName))
                {
                    invalidRows.Add(row);
                    continue;
                }

                studentId =
                    studentId.Trim();

                fullName =
                    fullName.Trim();

                var alreadyExists =
                    await _context.StudentRecords
                        .AnyAsync(x =>
                            x.StudentIdNumber ==
                            studentId);

                var alreadyInImport =
                    validRecords.Any(x =>
                        x.StudentIdNumber ==
                        studentId);

                if (
                    alreadyExists ||
                    alreadyInImport)
                {
                    duplicateRows.Add(row);
                    continue;
                }

                // =================================================
                // GET COURSE FIRST
                // =================================================

                var course =
                    GetCellValue(
                        worksheet,
                        row,
                        headerMap,
                        "Course");

                // =================================================
                // AUTOMATIC INSTITUTE
                // =================================================

                var institute =
                    GetInstituteFromCourse(
                        course);

                var record =
                    new StudentRecord
                    {
                        StudentIdNumber =
                            studentId,

                        FullName =
                            fullName,

                        Email =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "Email"),

                        Course =
                            course,

                        // AUTOMATIC INSTITUTE
                        Institute =
                            institute,

                        YearLevel =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "YearLevel"),

                        EnrollmentStatus =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "EnrollmentStatus")
                            ?? "Enrolled",

                        AcademicYear =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "AcademicYear")
                            ?? academicYear,

                        Semester =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "Semester")
                            ?? semester,

                        ImportBatchId =
                            batchId,

                        CreatedAt =
                            DateTime.Now,

                        UpdatedAt =
                            DateTime.Now
                    };

                validRecords.Add(record);
            }

            if (validRecords.Count == 0)
            {
                return BadRequest(new
                {
                    message =
                        "No valid student records were found.",

                    duplicateRows,

                    invalidRows
                });
            }

            _context.StudentRecords.AddRange(
                validRecords);

            _context.SchoolRecordImports.Add(
                new SchoolRecordImport
                {
                    BatchId =
                        batchId,

                    SourceType =
                        "Excel",

                    FileName =
                        file.FileName,

                    RecordType =
                        "Student",

                    AcademicYear =
                        academicYear,

                    Semester =
                        semester,

                    TotalRecords =
                        validRecords.Count,

                    ImportedBy =
                        adminId,

                    CreatedAt =
                        DateTime.Now
                });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message =
                    "Student records imported successfully.",

                batchId,

                imported =
                    validRecords.Count,

                duplicates =
                    duplicateRows.Count,

                invalid =
                    invalidRows.Count,

                duplicateRows,

                invalidRows
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new
                {
                    message =
                        "Failed to import student records.",

                    error =
                        ex.Message
                });
        }
    }

    // =========================================================
    // IMPORT FACULTY FROM EXCEL
    // =========================================================

    [HttpPost("faculty/import")]
    [RequestSizeLimit(MaxFileSize)]
    public async Task<IActionResult> ImportFaculty(
        IFormFile file,
        [FromQuery] int adminId,
        [FromQuery] string? academicYear = null)
    {
        if (file == null ||
            file.Length == 0)
        {
            return BadRequest(new
            {
                message =
                    "Please select an Excel file."
            });
        }

        if (file.Length > MaxFileSize)
        {
            return BadRequest(new
            {
                message =
                    "File must be 10 MB or smaller."
            });
        }

        var extension =
            Path.GetExtension(
                file.FileName)
                .ToLowerInvariant();

        if (extension != ".xlsx")
        {
            return BadRequest(new
            {
                message =
                    "Only .xlsx Excel files are supported."
            });
        }

        try
        {
            using var stream =
                file.OpenReadStream();

            using var workbook =
                new XLWorkbook(stream);

            var worksheet =
                workbook.Worksheets
                    .FirstOrDefault();

            if (worksheet == null)
            {
                return BadRequest(new
                {
                    message =
                        "The Excel file contains no worksheet."
                });
            }

            var headerMap =
                BuildHeaderMap(
                    worksheet);

            string[] requiredHeaders =
            {
                "FacultyIdNumber",
                "FullName"
            };

            var missingHeaders =
                requiredHeaders
                    .Where(x =>
                        !headerMap.ContainsKey(x))
                    .ToList();

            if (missingHeaders.Count > 0)
            {
                return BadRequest(new
                {
                    message =
                        "Missing required columns.",

                    missingColumns =
                        missingHeaders
                });
            }

            var batchId =
                $"FACULTY-{DateTime.Now:yyyyMMddHHmmssfff}";

            var validRecords =
                new List<FacultyRecord>();

            var duplicateRows =
                new List<int>();

            var invalidRows =
                new List<int>();

            var lastRow =
                worksheet.LastRowUsed()
                    ?.RowNumber() ?? 1;

            for (
                int row = 2;
                row <= lastRow;
                row++)
            {
                var facultyId =
                    GetCellValue(
                        worksheet,
                        row,
                        headerMap,
                        "FacultyIdNumber");

                var fullName =
                    GetCellValue(
                        worksheet,
                        row,
                        headerMap,
                        "FullName");

                if (
                    string.IsNullOrWhiteSpace(
                        facultyId) ||
                    string.IsNullOrWhiteSpace(
                        fullName))
                {
                    invalidRows.Add(row);
                    continue;
                }

                facultyId =
                    facultyId.Trim();

                fullName =
                    fullName.Trim();

                var alreadyExists =
                    await _context.FacultyRecords
                        .AnyAsync(x =>
                            x.FacultyIdNumber ==
                            facultyId);

                var alreadyInImport =
                    validRecords.Any(x =>
                        x.FacultyIdNumber ==
                        facultyId);

                if (
                    alreadyExists ||
                    alreadyInImport)
                {
                    duplicateRows.Add(row);
                    continue;
                }

                var record =
                    new FacultyRecord
                    {
                        FacultyIdNumber =
                            facultyId,

                        FullName =
                            fullName,

                        Email =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "Email"),

                        Institute =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "Institute"),

                        Position =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "Position"),

                        EmploymentStatus =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "EmploymentStatus")
                            ?? "Active",

                        AcademicYear =
                            GetCellValue(
                                worksheet,
                                row,
                                headerMap,
                                "AcademicYear")
                            ?? academicYear,

                        ImportBatchId =
                            batchId,

                        CreatedAt =
                            DateTime.Now,

                        UpdatedAt =
                            DateTime.Now
                    };

                validRecords.Add(record);
            }

            if (validRecords.Count == 0)
            {
                return BadRequest(new
                {
                    message =
                        "No valid faculty records were found.",

                    duplicateRows,

                    invalidRows
                });
            }

            _context.FacultyRecords.AddRange(
                validRecords);

            _context.SchoolRecordImports.Add(
                new SchoolRecordImport
                {
                    BatchId =
                        batchId,

                    SourceType =
                        "Excel",

                    FileName =
                        file.FileName,

                    RecordType =
                        "Faculty",

                    AcademicYear =
                        academicYear,

                    TotalRecords =
                        validRecords.Count,

                    ImportedBy =
                        adminId,

                    CreatedAt =
                        DateTime.Now
                });

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message =
                    "Faculty records imported successfully.",

                batchId,

                imported =
                    validRecords.Count,

                duplicates =
                    duplicateRows.Count,

                invalid =
                    invalidRows.Count,

                duplicateRows,

                invalidRows
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new
                {
                    message =
                        "Failed to import faculty records.",

                    error =
                        ex.Message
                });
        }
    }

    // =========================================================
    // HEADER MAPPING
    // =========================================================

    private static Dictionary<string, int> BuildHeaderMap(
        IXLWorksheet worksheet)
    {
        var result =
            new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

        var headerRow =
            worksheet.Row(1);

        foreach (var cell in
                 headerRow.CellsUsed())
        {
            var header =
                cell.GetString().Trim();

            if (!string.IsNullOrWhiteSpace(
                    header))
            {
                result[header] =
                    cell.Address.ColumnNumber;
            }
        }

        return result;
    }

    // =========================================================
    // CELL VALUE
    // =========================================================

    private static string? GetCellValue(
        IXLWorksheet worksheet,
        int row,
        Dictionary<string, int> headerMap,
        string header)
    {
        if (!headerMap.TryGetValue(
                header,
                out var column))
        {
            return null;
        }

        var value =
            worksheet
                .Cell(row, column)
                .GetString()
                .Trim();

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }
}