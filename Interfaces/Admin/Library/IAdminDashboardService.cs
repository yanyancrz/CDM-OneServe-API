using CDM_OneServe_API.DTOs.Admin.Library.Dashboard;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(CancellationToken cancellationToken);

    Task<List<OverdueBookDto>> GetOverdueAsync(int limit, CancellationToken cancellationToken);

    // userId = null -> whole team. userId = value -> only that person.
    Task<List<RecentActivityDto>> GetRecentActivityAsync(
        int limit, int currentUserId, bool allStaff, CancellationToken cancellationToken);
}