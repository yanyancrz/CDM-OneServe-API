using System.Globalization;
using System.Text.RegularExpressions;

namespace CDM_OneServe_API.Models.Library;

// Tracks a damaged copy that the patron still has to replace. The marker
// lives in the existing Remarks column, so no schema migration is needed.
//
//   [damaged-due:2026-11-05]       - replacement deadline
//   [damaged-resolved:2026-11-08]  - replacement received / copy repaired
public static class LibraryDamagedReturns
{
    public const int DefaultReplacementDays = 7;

    private static readonly Regex DuePattern = new(
        @"\[damaged-due:(\d{4}-\d{2}-\d{2})\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ResolvedPattern = new(
        @"\[damaged-resolved:(\d{4}-\d{2}-\d{2})\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExtraSpace = new(
        @"\s{2,}",
        RegexOptions.Compiled);

    public static string WithDueDate(string? remarks, DateTime dueDate)
    {
        var cleaned = Strip(ResolvedPattern, Strip(DuePattern, remarks)).Trim();
        var marker = $"[damaged-due:{dueDate:yyyy-MM-dd}]";

        return cleaned.Length == 0 ? marker : $"{cleaned} {marker}";
    }

    public static string MarkResolved(string? remarks, DateTime resolvedAt)
    {
        var cleaned = Strip(ResolvedPattern, remarks).Trim();
        var marker = $"[damaged-resolved:{resolvedAt:yyyy-MM-dd}]";

        return cleaned.Length == 0 ? marker : $"{cleaned} {marker}";
    }

    public static DateTime? GetDueDate(string? remarks) => Extract(DuePattern, remarks);

    public static DateTime? GetResolvedDate(string? remarks) => Extract(ResolvedPattern, remarks);

    public static bool IsResolved(string? remarks) => GetResolvedDate(remarks) != null;

    private static DateTime? Extract(Regex pattern, string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks)) return null;

        var match = pattern.Match(remarks);

        return match.Success &&
               DateTime.TryParseExact(
                   match.Groups[1].Value,
                   "yyyy-MM-dd",
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out var date)
            ? date
            : null;
    }

    private static string Strip(Regex pattern, string? remarks)
    {
        if (string.IsNullOrWhiteSpace(remarks)) return string.Empty;

        return ExtraSpace.Replace(pattern.Replace(remarks, string.Empty), " ").Trim();
    }
}
