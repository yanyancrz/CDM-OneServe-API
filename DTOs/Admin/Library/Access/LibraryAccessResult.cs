namespace CDM_OneServe_API.DTOs.Admin.Library.Access;

public enum LibraryAccessFailure
{
    None = 0,
    UserNotFound,
    AccountInactive,
    NotLibraryRole
}

public class LibraryAccessResult
{
    public LibraryAccessDto? Access { get; init; }

    public LibraryAccessFailure Failure { get; init; }

    public static LibraryAccessResult Success(LibraryAccessDto access) => new()
    {
        Access = access,
        Failure = LibraryAccessFailure.None
    };

    public static LibraryAccessResult Denied(LibraryAccessFailure failure) => new()
    {
        Failure = failure
    };
}