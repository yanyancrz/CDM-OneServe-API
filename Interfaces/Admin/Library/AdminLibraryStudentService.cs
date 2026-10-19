using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Students;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

// READ ONLY. This service never writes to Users.
public class AdminLibraryStudentService : IAdminLibraryStudentService
{
    private const string StudentRole = "Student";
    private const int HistoryLimit = 100;

    private readonly AppDbContext _db;

    public AdminLibraryStudentService(AppDbContext db)
    {
        _db = db;
    }

    private IQueryable<Models.User> Students() =>
        _db.Users.AsNoTracking().Where(u => u.Role == StudentRole);

    public async Task<PagedResult<StudentDto>> GetStudentsAsync(StudentFilterDto f, CancellationToken cancellationToken)
    {
        var page = Math.Max(f.Page, 1);
        var pageSize = Math.Clamp(f.PageSize, 1, 100);
        var now = DateTime.Now;

        var query = Students();

        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            query = query.Where(u =>
                u.FullName.Contains(s) ||
                (u.IdNumber != null && u.IdNumber.Contains(s)) ||
                (u.Email != null && u.Email.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(f.Course)) query = query.Where(u => u.Course == f.Course);
        if (!string.IsNullOrWhiteSpace(f.YearLevel)) query = query.Where(u => u.YearLevel == f.YearLevel);
        if (!string.IsNullOrWhiteSpace(f.Status)) query = query.Where(u => u.AccountStatus == f.Status);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new StudentDto
            {
                UserId = u.Id,
                IdNumber = u.IdNumber,
                FullName = u.FullName,
                Email = u.Email,
                Course = u.Course,
                YearLevel = u.YearLevel,
                AccountStatus = u.AccountStatus,
                ActiveLoans = _db.BorrowTransactions.Count(t => t.UserId == u.Id && t.ReturnDate == null),
                OverdueLoans = _db.BorrowTransactions.Count(t =>
                    t.UserId == u.Id && t.ReturnDate == null && t.DueDate < now)
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<StudentDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<List<string>> GetCoursesAsync(CancellationToken cancellationToken)
    {
        return await Students()
            .Where(u => u.Course != null && u.Course != "")
            .Select(u => u.Course!)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);
    }

    public async Task<LibraryResult<StudentDto>> GetStudentAsync(int id, CancellationToken cancellationToken)
    {
        var now = DateTime.Now;

        var student = await Students()
            .Where(u => u.Id == id)
            .Select(u => new StudentDto
            {
                UserId = u.Id,
                IdNumber = u.IdNumber,
                FullName = u.FullName,
                Email = u.Email,
                Course = u.Course,
                YearLevel = u.YearLevel,
                AccountStatus = u.AccountStatus,
                ActiveLoans = _db.BorrowTransactions.Count(t => t.UserId == u.Id && t.ReturnDate == null),
                OverdueLoans = _db.BorrowTransactions.Count(t =>
                    t.UserId == u.Id && t.ReturnDate == null && t.DueDate < now)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return student == null
            ? LibraryResult<StudentDto>.Failure(404, "Student not found.")
            : LibraryResult<StudentDto>.Success(student);
    }

    public async Task<LibraryResult<List<StudentLoanDto>>> GetActiveLoansAsync(int id, CancellationToken cancellationToken)
    {
        if (!await Students().AnyAsync(u => u.Id == id, cancellationToken))
            return LibraryResult<List<StudentLoanDto>>.Failure(404, "Student not found.");

        var rows = await LoanRows(id)
            .Where(r => r.ReturnDate == null)
            .OrderBy(r => r.DueDate)
            .ToListAsync(cancellationToken);

        return LibraryResult<List<StudentLoanDto>>.Success(rows.Select(ToLoanDto).ToList());
    }

    public async Task<LibraryResult<List<StudentLoanDto>>> GetHistoryAsync(int id, CancellationToken cancellationToken)
    {
        if (!await Students().AnyAsync(u => u.Id == id, cancellationToken))
            return LibraryResult<List<StudentLoanDto>>.Failure(404, "Student not found.");

        // Full history: returned and still-active loans, newest first.
        var rows = await LoanRows(id)
            .OrderByDescending(r => r.BorrowDate)
            .Take(HistoryLimit)
            .ToListAsync(cancellationToken);

        return LibraryResult<List<StudentLoanDto>>.Success(rows.Select(ToLoanDto).ToList());
    }

    public async Task<LibraryResult<List<StudentReservationDto>>> GetReservationsAsync(int id, CancellationToken cancellationToken)
    {
        if (!await Students().AnyAsync(u => u.Id == id, cancellationToken))
            return LibraryResult<List<StudentReservationDto>>.Failure(404, "Student not found.");

        var items = await _db.Reservations.AsNoTracking()
            .Where(r => r.UserId == id)
            .OrderByDescending(r => r.ReservationAt)
            .Take(HistoryLimit)
            .Select(r => new StudentReservationDto
            {
                ReservationId = r.ReservationId,
                BookTitle = r.Book != null ? r.Book.Title : "(deleted book)",
                ReservationAt = r.ReservationAt,
                ExpirationDate = r.ExpirationDate,
                Status = r.Status
            })
            .ToListAsync(cancellationToken);

        return LibraryResult<List<StudentReservationDto>>.Success(items);
    }

    // ----- helpers -----

    private sealed record LoanRow(
        int BorrowId, int BookId, string Title, string Author,
        DateTime BorrowDate, DateTime DueDate, DateTime? ReturnDate,
        string Status);

    private IQueryable<LoanRow> LoanRows(int userId) =>
        _db.BorrowTransactions.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new LoanRow(
                t.BorrowId,
                t.BookId,
                t.Book != null ? t.Book.Title : "(deleted book)",
                t.Book != null ? t.Book.Author : "",
                t.BorrowDate,
                t.DueDate,
                t.ReturnDate,
                t.Status));

    private static StudentLoanDto ToLoanDto(LoanRow r)
    {
        var now = DateTime.Now;
        var isActive = r.ReturnDate == null;
        var isOverdue = isActive && r.DueDate < now;

        var overdueDays = isOverdue
            ? Math.Max((now.Date - r.DueDate.Date).Days, 0)
            : 0;

        return new StudentLoanDto
        {
            TransactionId = r.BorrowId,
            BookId = r.BookId,
            BookTitle = r.Title,
            Author = r.Author,
            BorrowDate = r.BorrowDate,
            DueDate = r.DueDate,
            ReturnDate = r.ReturnDate,
            Status = r.Status,
            IsOverdue = isOverdue,
            OverdueDays = overdueDays
        };
    }
}