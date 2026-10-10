namespace CDM_OneServe_API.DTOs.Admin.Library.Access;

/// <summary>
/// What the signed-in Library user is allowed to do.
/// Computed on the server and sent to the frontend ONLY so the UI can hide
/// buttons and menu items. Every endpoint still enforces its own authorization.
/// </summary>
public class LibraryPermissionsDto
{
    public bool CanViewDashboard { get; init; }
    public bool CanManageBooks { get; init; }       // add / edit
    public bool CanDeleteBooks { get; init; }
    public bool CanViewPatrons { get; init; }       // students + faculty (read only)
    public bool CanManageCirculation { get; init; } // borrow / return
    public bool CanManageReservations { get; init; }
    public bool CanViewReports { get; init; }
    public bool CanManageStaff { get; init; }
    public bool CanManageSettings { get; init; }
    public bool CanEditTerms { get; init; }
}

public class LibraryAccessDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string RoleLabel { get; init; } = string.Empty;
    public bool IsHead { get; init; }
    public bool IsStaff { get; init; }
    public string? AdminModule { get; init; }
    public LibraryPermissionsDto Permissions { get; init; } = new();
}