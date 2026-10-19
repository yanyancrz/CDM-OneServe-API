using System.Security.Claims;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin.Library.Policy;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Library;

public class LibraryPolicyService : ILibraryPolicyService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public LibraryPolicyService(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task<LibraryPolicyDto> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var policy = new LibraryPolicyDto
        {
            // The live values. The Library Head's Settings page updates these
            // (and Program.cs loads them from the database at startup).
            Settings = new LibraryPolicySettingsDto
            {
                StudentBorrowLimit = LibraryDefaults.StudentBorrowLimit,
                FacultyBorrowLimit = LibraryDefaults.FacultyBorrowLimit,
                DefaultLoanDays = LibraryDefaults.DefaultLoanDays,
                ReservationPickupDays = LibraryDefaults.ReservationPickupDays,
                FirstSemStart = LibraryDefaults.FirstSemStart,
                FirstSemEnd = LibraryDefaults.FirstSemEnd,
                SecondSemStart = LibraryDefaults.SecondSemStart,
                SecondSemEnd = LibraryDefaults.SecondSemEnd,
                LoanDayOptions = LibraryDefaults.LoanDayOptions
            },

            // The Terms & Guidelines are added in the next step,
            // once the LibraryTerm model is available.
            Terms = new List<LibraryPolicyTermDto>()
        };

        // The signed-in patron's own borrowing suspension, so the app can show
        // it. Left null when the user cannot be resolved.
        var idClaim = _http.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (int.TryParse(idClaim, out var userId))
        {
            policy.BannedUntil = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.BorrowBanUntil)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return policy;
    }
}
