namespace CDM_OneServe_API.Models.Platform;

/// <summary>
/// The four levels a capability can be held at. Ordered, so a check is a
/// simple "is the held level at least the required one?".
///
///   view          - read dashboards, lists, reports
///   manage        - create / edit / approve / correct records
///   full_control  - settings, staff, module access, destructive actions
/// </summary>
public static class PermissionLevels
{
    public const string None = "none";
    public const string View = "view";
    public const string Manage = "manage";
    public const string FullControl = "full_control";

    public static int Rank(string? level) => level switch
    {
        View => 1,
        Manage => 2,
        FullControl => 3,
        _ => 0
    };

    public static bool IsKnown(string? level) =>
        level is None or View or Manage or FullControl;

    /// <summary>The higher of two levels (used to merge role + override).</summary>
    public static string Max(string? a, string? b) =>
        Rank(a) >= Rank(b) ? (IsKnown(a) ? a! : None) : b!;
}

/// <summary>
/// One capability the admin portal understands.
/// </summary>
/// <param name="Key">Stable key written to the database.</param>
/// <param name="Label">Text shown in the permission matrix.</param>
/// <param name="Category">Grouping on the matrix page.</param>
/// <param name="Description">What the capability actually allows.</param>
/// <param name="DefaultAdmin">Default level for the built-in Admin role.</param>
/// <param name="DefaultSuperAdmin">Default level for SuperAdmin.</param>
/// <param name="SuperAdminOnly">Cannot be granted to Admin through the matrix.</param>
public sealed record PermissionDefinition(
    string Key,
    string Label,
    string Category,
    string Description,
    string DefaultAdmin,
    string DefaultSuperAdmin,
    bool SuperAdminOnly = false);

/// <summary>
/// Single source of truth for platform-admin capabilities.
///
/// IMPORTANT: these keys only govern the CENTRAL admin portal and module
/// access. The module-native consoles (Library Head, Guidance Head, Lost &amp;
/// Found admins) keep their own long-standing server-side role checks, which
/// are not weakened or replaced by this catalog.
/// </summary>
public static class PermissionCatalog
{
    public const string AdminRole = "Admin";
    public const string SuperAdminRole = "SuperAdmin";

    // ---- general ----
    public const string DashboardView = "admin.dashboard.view";
    public const string UsersManage = "admin.users.manage";
    public const string RolesManage = "admin.roles.manage";
    public const string AuditView = "admin.audit.view";
    public const string SecurityView = "admin.security.view";
    public const string SecurityManage = "admin.security.manage";
    public const string MonitoringView = "admin.monitoring.view";
    public const string SettingsManage = "admin.settings.manage";
    public const string AnnouncementsManage = "admin.announcements.manage";
    public const string BackupView = "admin.backup.view";
    public const string BackupRequest = "admin.backup.request";
    public const string BackupRestore = "admin.backup.restore";

    // ---- services ----
    public const string LibraryView = "service.library.view";
    public const string LibraryManage = "service.library.manage";
    public const string ClinicView = "service.clinic.view";
    public const string ClinicManage = "service.clinic.manage";
    public const string GuidanceView = "service.guidance.view";
    public const string GuidanceManage = "service.guidance.manage";
    public const string LostFoundView = "service.lostfound.view";
    public const string LostFoundManage = "service.lostfound.manage";

    public static readonly IReadOnlyList<PermissionDefinition> All = new List<PermissionDefinition>
    {
        new(DashboardView, "View dashboard", "General",
            "See platform totals, trends and module activity.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(UsersManage, "Manage users", "General",
            "Create, edit, deactivate and reset OneServe accounts.",
            PermissionLevels.Manage, PermissionLevels.FullControl),

        new(RolesManage, "Manage roles & permissions", "General",
            "Change the permission matrix and per-user overrides.",
            PermissionLevels.Manage, PermissionLevels.FullControl),

        new(AuditView, "View audit logs", "System",
            "Read and export the central audit trail. Records are append-only.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(SecurityView, "View security center", "System",
            "See login attempts, active sessions and security alerts.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(SecurityManage, "Manage security", "System",
            "Revoke sessions and sign accounts out of every device.",
            PermissionLevels.Manage, PermissionLevels.FullControl),

        new(MonitoringView, "View system monitoring", "System",
            "See live health checks and health history.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(SettingsManage, "Manage system settings", "System",
            "Change platform settings, module availability and maintenance mode.",
            PermissionLevels.Manage, PermissionLevels.FullControl),

        new(AnnouncementsManage, "Manage announcements", "System",
            "Create, schedule, archive and broadcast announcements.",
            PermissionLevels.Manage, PermissionLevels.FullControl),

        new(BackupView, "View backups", "System",
            "See backup history, status and database information.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(BackupRequest, "Request a backup", "System",
            "Submit a database backup request to the DBA / Azure operator.",
            PermissionLevels.Manage, PermissionLevels.FullControl),

        new(BackupRestore, "Approve restores", "System",
            "Approve or reject database restore requests. SuperAdmin only.",
            PermissionLevels.None, PermissionLevels.FullControl, SuperAdminOnly: true),

        new(LibraryView, "Library - view", "Services",
            "Open the Library oversight dashboard, patrons, loans and reports (read-only).",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(LibraryManage, "Library - manage", "Services",
            "Correct loans, approve reservations and add books from the central portal.",
            PermissionLevels.FullControl, PermissionLevels.FullControl),

        new(ClinicView, "Clinic - view", "Services",
            "See Clinic CMS health, usage counts and sanitised integration logs.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(ClinicManage, "Clinic - manage", "Services",
            "Test the CMS connection, enable/disable Clinic access and manage integration settings.",
            PermissionLevels.FullControl, PermissionLevels.FullControl),

        new(GuidanceView, "Guidance - view", "Services",
            "See counselor, appointment and report COUNTS. Never narrative contents.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(GuidanceManage, "Guidance - manage", "Services",
            "Manage counselors, appointments and availability.",
            PermissionLevels.FullControl, PermissionLevels.FullControl),

        new(LostFoundView, "Lost & Found - view", "Services",
            "See reports, claims, schedules and module activity.",
            PermissionLevels.View, PermissionLevels.FullControl),

        new(LostFoundManage, "Lost & Found - manage", "Services",
            "Approve claims, reschedule pickups and manage module administrators.",
            PermissionLevels.FullControl, PermissionLevels.FullControl),
    };

    private static readonly Dictionary<string, PermissionDefinition> ByKey =
        All.ToDictionary(x => x.Key, StringComparer.Ordinal);

    public static bool IsKnown(string? key) =>
        key is not null && ByKey.ContainsKey(key);

    public static PermissionDefinition? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var def) ? def : null;

    /// <summary>
    /// The level a role holds when the database has no row for it.
    /// Module-owner roles (LibraryAdmin, GuidanceAdmin, Counselor, ...) are
    /// intentionally absent: they are governed by their own consoles.
    /// </summary>
    public static string DefaultLevel(string? role, string key)
    {
        var def = Find(key);
        if (def is null) return PermissionLevels.None;

        return role switch
        {
            SuperAdminRole => def.DefaultSuperAdmin,
            AdminRole => def.DefaultAdmin,
            _ => PermissionLevels.None
        };
    }

    /// <summary>Module access keys, used by the service pages' guard.</summary>
    public static string ServiceViewKey(string module) => module?.Trim().ToLowerInvariant() switch
    {
        "library" => LibraryView,
        "clinic" => ClinicView,
        "guidance" => GuidanceView,
        "lost & found" or "lostfound" or "lost-found" => LostFoundView,
        _ => ""
    };
}
