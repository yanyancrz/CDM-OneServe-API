using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminReturnService
{
    // Active (unreturned) loans, searchable.
    Task<PagedResult<ReturnLoanDto>> GetActiveLoansAsync(ReturnFilterDto filter, CancellationToken cancellationToken);

    // One active loan with its current fine.
    Task<LibraryResult<ReturnLoanDto>> LookupAsync(int transactionId, CancellationToken cancellationToken);

    // One database transaction. The fine is computed here, never trusted from the client.
    Task<LibraryResult<ReturnResultDto>> ReturnAsync(ReturnBookDto dto, int staffId, CancellationToken cancellationToken);
}