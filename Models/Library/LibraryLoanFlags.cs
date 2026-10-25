namespace CDM_OneServe_API.Models.Library;

// Small marker used when a loan is for inside-library use only. It lives in
// the existing Remarks column so this rule does not need a schema migration.
public static class LibraryLoanFlags
{
    public const string InsideUse = "[Inside library use only]";

    public static bool IsInsideUse(string? remarks) =>
        !string.IsNullOrWhiteSpace(remarks) &&
        remarks.Contains(InsideUse, StringComparison.OrdinalIgnoreCase);

    public static string WithInsideUse(string? remarks) =>
        string.IsNullOrWhiteSpace(remarks)
            ? InsideUse
            : $"{remarks.Trim()} {InsideUse}";
}
