namespace CDM_OneServe_API.DTOs;

public class RegisterRequest
{
    // =========================================================
    // NEW FLOW: registration sends ONLY IdNumber + Email + Role.
    // Names are completed later in Setup Profile, and the
    // password (login credentials) is generated server-side
    // and emailed to the user.
    //
    // FirstName / MiddleName / LastName / Password
    // stay optional so older app versions
    // still deserialize — the new Register endpoint ignores them.
    // =========================================================

    public string FirstName { get; set; } = "";

    public string MiddleName { get; set; } = "";

    public string LastName { get; set; } = "";

    public string IdNumber { get; set; } = "";

    public string Email { get; set; } = "";

    public string Password { get; set; } = "";

    public string Role { get; set; } = "Student";
}
