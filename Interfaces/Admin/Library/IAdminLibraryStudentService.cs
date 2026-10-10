using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Students;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminLibraryStudentService
{
    Task<PagedResult<StudentDto>> GetStudentsAsync(StudentFilterDto filter, CancellationToken cancellationToken);

    Task<List<string>> GetCoursesAsync(CancellationToken cancellationToken);

    Task<LibraryResult<StudentDto>> GetStudentAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<List<StudentLoanDto>>> GetActiveLoansAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<List<StudentLoanDto>>> GetHistoryAsync(int id, CancellationToken cancellationToken);

    Task<LibraryResult<List<StudentReservationDto>>> GetReservationsAsync(int id, CancellationToken cancellationToken);
}