namespace CDM_OneServe_API.DTOs.LostFound;

// =============================================================
// LOST & FOUND ADMINISTRATION / AUDIT DTOs
// =============================================================

/// <summary>Create a delegated Lost &amp; Found administrator.</summary>
public class CreateSubAdminDto
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>Official email, also the login username.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Initial password - hashed with BCrypt, never stored or returned.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Activate or deactivate a delegated administrator.</summary>
public class UpdateAdminStatusDto
{
    public bool IsActive { get; set; }
}

/// <summary>The signed-in console user, plus what they are allowed to do.</summary>
public class LostFoundAdminMeDto
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string RoleLabel { get; set; } = string.Empty;

    public bool IsSuperAdmin { get; set; }

    public bool CanManageAdmins { get; set; }

    public bool MustChangePassword { get; set; }
}

/// <summary>A delegated administrator as the management page sees it.</summary>
public class SubAdminDto
{
    public int Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string RoleLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }
}

/// <summary>One admin login session, with a server-derived status.</summary>
public class AdminLoginActivityDto
{
    public int SessionId { get; set; }

    public int UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string RoleLabel { get; set; } = string.Empty;

    public DateTime LoginAt { get; set; }

    public DateTime? LogoutAt { get; set; }

    public DateTime LastActivityAt { get; set; }

    /// <summary>Active | Logged Out | Session Expired</summary>
    public string SessionStatus { get; set; } = "Active";

    public string? DeviceLabel { get; set; }

    public string? IpAddress { get; set; }

    public string? LogoutReason { get; set; }

    public bool IsCurrentSession { get; set; }
}

/// <summary>One append-only audit row.</summary>
public class AdminActivityDto
{
    public int Id { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public string ActorRole { get; set; } = string.Empty;

    public string ActorRoleLabel { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public int? EntityId { get; set; }

    public string? PreviousStatus { get; set; }

    public string? NewStatus { get; set; }

    public string? Details { get; set; }

    public DateTime Timestamp { get; set; }
}

/// <summary>Paged audit response.</summary>
public class AdminActivityPageDto
{
    public List<AdminActivityDto> Items { get; set; } = new();

    public int Total { get; set; }
}
