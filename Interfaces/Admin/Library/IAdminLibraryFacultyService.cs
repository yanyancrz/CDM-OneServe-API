using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Faculty;
using CDM_OneServe_API.DTOs.Admin.Library.Students;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminLibraryFacultyService
{
    Task<PagedResult<FacultyDto>> GetFacultyAsync(FacultyFilterDto filter, CancellationToken cancellationToken);

    Task<List<string>> GetDepartmentsAsync(CancellationToken cancellationToken);

    Task<LibraryResult<FacultyDto>> GetFacultyMemberAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<List<StudentLoanDto>>> GetActiveLoansAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<List<StudentLoanDto>>> GetHistoryAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<List<StudentReservationDto>>> GetReservationsAsync(int id, CancellationToken cancellationToken);
}