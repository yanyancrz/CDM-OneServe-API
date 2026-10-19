using CDM_OneServe_API.Models;

namespace CDM_OneServe_API.Models;

public class User
{
    public int Id { get; set; }

    public string IdNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string Email { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public string? Course { get; set; }

    public string? YearLevel { get; set; }

    public string? ContactNumber { get; set; }

    public string? ProfilePicture { get; set; }

    public bool IsVerified { get; set; }

    public bool IsProfileComplete { get; set; }

    // True while the account still uses the password we generated and
    // emailed at registration. The dashboard nudges the user to change it,
    // and any password change (Profile or OTP flow) clears it.
    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; }
        = DateTime.Now;

    /// <summary>
    /// Every token issued before this moment is dead, which is what makes
    /// "Log out on all devices" possible with a stateless JWT. Null means
    /// "nothing was ever revoked" - the normal case.
    ///
    /// The login writes the token's issued-at into the <c>iat</c> claim and
    /// <c>OnTokenValidated</c> in Program.cs compares the two, so a value
    /// here cannot lock out a token minted afterwards. Changing a password
    /// can bump the same field.
    /// </summary>
    public DateTime? TokensValidFrom { get; set; }

    public string Role { get; set; } = "Student";

    public string? AdminModule { get; set; }

    public string? Institute { get; set; }

    public string? StudentStatus { get; set; }

    public string? Position { get; set; }

    // Registration Verification Status
    public string AccountStatus { get; set; }
        = "Pending";

    // Set when a book is returned late: the patron cannot borrow again until
    // this date (the end of the semester in progress). Null means no ban.
    public DateTime? BorrowBanUntil { get; set; }
}