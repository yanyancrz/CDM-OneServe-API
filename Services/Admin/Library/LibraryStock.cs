using CDM_OneServe_API.Data;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

/// <summary>
/// Atomic stock changes for reservations (admin and mobile).
/// Each change is a single UPDATE ... WHERE, so two requests can never both take the last copy.
/// Call these inside the caller's transaction.
/// </summary>
public static class LibraryStock
{
    /// <summary>Takes one copy off the shelf. Returns false when no copy is available.</summary>
    public static async Task<bool> TryHoldCopyAsync(
        AppDbContext db, int bookId, DateTime now, CancellationToken cancellationToken = default)
    {
        var taken = await db.Books
            .Where(b => b.BookId == bookId && b.AvailableCopies > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.AvailableCopies, b => b.AvailableCopies - 1)
                .SetProperty(b => b.UpdatedAt, now), cancellationToken);

        if (taken == 0) return false;

        await db.Books
            .Where(b => b.BookId == bookId && b.AvailableCopies <= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, "Unavailable"), cancellationToken);

        return true;
    }

    /// <summary>Puts one held copy back on the shelf (never above TotalCopies).</summary>
    public static async Task ReleaseCopyAsync(
        AppDbContext db, int bookId, DateTime now, CancellationToken cancellationToken = default)
    {
        await db.Books
            .Where(b => b.BookId == bookId && b.AvailableCopies < b.TotalCopies)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.AvailableCopies, b => b.AvailableCopies + 1)
                .SetProperty(b => b.UpdatedAt, now), cancellationToken);

        await db.Books
            .Where(b => b.BookId == bookId && b.AvailableCopies > 0 && b.Status == "Unavailable")
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, "Available"), cancellationToken);
    }
}
