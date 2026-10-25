using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Terms;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Models.Library;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class AdminTermsService : IAdminTermsService
{
    private const int MaxContentLength = 4000;

    private readonly AppDbContext _db;
    private readonly IAdminLibraryActivityLogger _activity;

    public AdminTermsService(AppDbContext db, IAdminLibraryActivityLogger activity)
    {
        _db = db;
        _activity = activity;
    }

    // The sections are fixed. Only their content can be edited.
    private static List<(string Key, string Title, string Content)> Defaults() => new()
    {
        ("borrowing-quotas", "Borrowing Quotas",
            $"Students may borrow up to {LibraryDefaults.StudentBorrowLimit} books at a time. " +
            $"Faculty may borrow up to {LibraryDefaults.FacultyBorrowLimit} books at a time."),
        ("loan-duration", "Loan Duration",
            $"Books are lent for {LibraryDefaults.DefaultLoanDays} days by default. " +
            "The borrower may be given 3, 7, or 14 days at the counter."),
        ("inside-library-use", "Inside Library Use",
            "When only one copy of a title remains available, that copy may be used inside the library only. " +
            "It must be used during library hours, 8:00 AM to 5:00 PM, and returned to the counter before " +
            "closing on the same day. The 3, 7, or 14-day home loan period does not apply to this copy."),
        ("renewal-policies", "Renewal Policies",
            "A book may be renewed if no other patron has reserved it and it is not overdue. Ask the librarian at the counter."),
        ("overdue-penalty", "Overdue Penalty",
            "There are no cash fines. Returning a book after its due date immediately suspends the borrower " +
            "from borrowing for the rest of the current semester. Borrowing is allowed again starting the next semester."),
        ("lost-book-policy", "Lost Book Policy",
            "A lost book must be reported to the library right away. The borrower must replace it with an exact " +
            "copy of the same book - same ISBN and edition. Cash payment is not accepted in place of the replacement."),
        ("damaged-book-policy", "Damaged Book Policy",
            "Books must be returned in the condition they were borrowed. A damaged book is assessed by the " +
            "librarian; if it can no longer be used, the borrower must replace it with an exact copy of the same " +
            "book - same ISBN and edition."),
        ("library-hours", "Library Hours",
            "Monday to Friday, 8:00 AM to 5:00 PM. The library is closed on weekends and holidays."),
        ("ebook-rules", "eBook Rules",
            "eBooks are for reading inside CDM OneServe only. Copying, sharing, or redistributing them is not allowed."),
        ("collection-scope", "Collection Scope",
            "The library lends only ITE, IBE, and ICS subject-related books.")
    };

    private static LibraryTermDto ToDto(LibraryTerm t) => new()
    {
        TermId = t.TermId,
        TermKey = t.TermKey,
        Title = t.Title,
        Content = t.Content,
        SortOrder = t.SortOrder,
        UpdatedAt = t.UpdatedAt
    };

    public async Task<List<LibraryTermDto>> GetAsync(CancellationToken cancellationToken)
    {
        var existing = await _db.LibraryTerms.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ToListAsync(cancellationToken);

        var existingKeys = existing
            .Select(t => t.TermKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Seed the defaults on a new install, and add any new section (for
        // example, Inside Library Use) to a database that was seeded before.
        var missing = Defaults()
            .Where(item => !existingKeys.Contains(item.Key))
            .ToList();

        if (missing.Count > 0)
        {
            var now = DateTime.Now;
            var order = existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder);

            foreach (var (key, title, content) in missing)
                _db.LibraryTerms.Add(new LibraryTerm
                {
                    TermKey = key,
                    Title = title,
                    Content = content,
                    SortOrder = ++order,
                    UpdatedAt = now
                });

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another request seeded at the same moment (unique TermKey). Read what is there.
                _db.ChangeTracker.Clear();
            }
        }

        var rows = await _db.LibraryTerms.AsNoTracking()
            .OrderBy(t => t.SortOrder)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<LibraryResult<List<LibraryTermDto>>> UpdateAsync(
        UpdateTermsDto dto, int headId, CancellationToken cancellationToken)
    {
        if (dto.Sections == null || dto.Sections.Count == 0)
            return LibraryResult<List<LibraryTermDto>>.Failure(400, "There is nothing to save.");

        // Make sure the rows exist before editing them.
        await GetAsync(cancellationToken);

        var rows = await _db.LibraryTerms.ToListAsync(cancellationToken);
        var now = DateTime.Now;
        var changed = new List<string>();

        foreach (var item in dto.Sections)
        {
            var row = rows.FirstOrDefault(r => r.TermKey == item.TermKey);

            if (row == null)
                return LibraryResult<List<LibraryTermDto>>.Failure(400, $"Unknown section: {item.TermKey}.");

            var content = (item.Content ?? string.Empty).Trim();

            if (content.Length == 0)
                return LibraryResult<List<LibraryTermDto>>.Failure(400, $"\"{row.Title}\" cannot be empty.");

            if (content.Length > MaxContentLength)
                return LibraryResult<List<LibraryTermDto>>.Failure(400,
                    $"\"{row.Title}\" must be {MaxContentLength} characters or fewer.");

            if (row.Content == content) continue;

            row.Content = content;
            row.UpdatedAt = now;
            row.UpdatedBy = headId;
            changed.Add(row.Title);
        }

        if (changed.Count == 0)
            return LibraryResult<List<LibraryTermDto>>.Success(await GetAsync(cancellationToken));

        _activity.Add(headId, "LIBRARY_TERMS_UPDATED", $"edited the library terms ({string.Join(", ", changed)})");

        await _db.SaveChangesAsync(cancellationToken);

        return LibraryResult<List<LibraryTermDto>>.Success(await GetAsync(cancellationToken));
    }
}