using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;
using CDM_OneServe_API.Models.Library;
using CDM_OneServe_API.Services.Admin.Library;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Library;

// A patron with an unresolved damaged return cannot borrow another book
// until the damaged copy is replaced or the obligation is settled. Every
// borrow/claim path asks this helper before it changes stock or creates a
// loan, so the app and the Scanner always agree on the same block.
public static class LibraryBorrowBlock
{
    public static async Task<List<DamagedReturnDto>> GetOpenDamagedReturnsAsync(
        AppDbContext db,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var today = LibraryClock.Now.Date;

        var rows = await db.BorrowTransactions.AsNoTracking()
            .Include(t => t.Book)
            .Include(t => t.User)
            .Where(t =>
                t.UserId == userId &&
                t.ReturnDate != null &&
                t.ReturnCondition == "Damaged")
            .OrderByDescending(t => t.ReturnDate)
            .ToListAsync(cancellationToken);

        // The resolved marker lives in Remarks, so the filter is done after
        // materialising this small per-patron set.
        return rows
            .Where(t => !LibraryDamagedReturns.IsResolved(t.Remarks))
            .Select(t =>
            {
                var dueDate = LibraryDamagedReturns.GetDueDate(t.Remarks);

                return new DamagedReturnDto
                {
                    TransactionId = t.BorrowId,
                    UserId = t.UserId,
                    PatronName = t.User?.FullName ?? "(deleted account)",
                    PatronId = t.User?.IdNumber,
                    PatronType = t.User?.Role ?? string.Empty,
                    BookId = t.BookId,
                    BookTitle = t.Book?.Title ?? "(deleted book)",
                    Author = t.Book?.Author ?? string.Empty,
                    ISBN = t.Book?.ISBN,
                    CoverImage = t.Book?.CoverImage,
                    ReturnDate = t.ReturnDate ?? t.DueDate,
                    ReplacementDueDate = dueDate,
                    IsOverdue = dueDate.HasValue && dueDate.Value.Date < today,
                    DaysLeft = dueDate.HasValue
                        ? (dueDate.Value.Date - today).Days
                        : null,
                    IsResolved = false
                };
            })
            .OrderBy(x => x.ReplacementDueDate ?? DateTime.MaxValue)
            .ThenByDescending(x => x.ReturnDate)
            .ToList();
    }

    public static async Task<string?> GetBlockMessageAsync(
        AppDbContext db,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var items = await GetOpenDamagedReturnsAsync(db, userId, cancellationToken);

        return BuildMessage(items);
    }

    public static string? BuildMessage(IReadOnlyList<DamagedReturnDto> items)
    {
        if (items.Count == 0) return null;

        var first = items[0];
        var due = first.ReplacementDueDate.HasValue
            ? $" (replacement due {first.ReplacementDueDate.Value:MMM d, yyyy})"
            : string.Empty;

        var more = items.Count > 1
            ? $" and {items.Count - 1} other damaged book(s)"
            : string.Empty;

        return $"Borrowing is blocked until the damaged copy of " +
               $"'{first.BookTitle}'{more} is replaced or settled{due}.";
    }
}
