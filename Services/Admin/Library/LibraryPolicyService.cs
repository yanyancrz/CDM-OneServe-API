using CDM_OneServe_API.DTOs.Admin.Library.Policy;
using CDM_OneServe_API.Interfaces.Admin.Library;

namespace CDM_OneServe_API.Services.Admin.Library;

public class LibraryPolicyService : ILibraryPolicyService
{
    public Task<LibraryPolicyDto> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var policy = new LibraryPolicyDto
        {
            // The live values. The Library Head's Settings page updates these
            // (and Program.cs loads them from the database at startup).
            Settings = new LibraryPolicySettingsDto
            {
                OverdueFinePerDay = LibraryDefaults.OverdueFinePerDay,
                StudentBorrowLimit = LibraryDefaults.StudentBorrowLimit,
                FacultyBorrowLimit = LibraryDefaults.FacultyBorrowLimit,
                DefaultLoanDays = LibraryDefaults.DefaultLoanDays,
                ReservationPickupDays = LibraryDefaults.ReservationPickupDays,
                LoanDayOptions = LibraryDefaults.LoanDayOptions
            },

            // The Terms & Guidelines are added in the next step,
            // once the LibraryTerm model is available.
            Terms = new List<LibraryPolicyTermDto>()
        };

        return Task.FromResult(policy);
    }
}