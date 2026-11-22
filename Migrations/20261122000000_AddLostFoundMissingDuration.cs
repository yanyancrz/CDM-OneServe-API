using CDM_OneServe_API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CDM_OneServe_API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122000000_AddLostFoundMissingDuration")]
public class AddLostFoundMissingDuration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("FinalMissingDays", "LostFoundReports", type: "int", nullable: true);
        migrationBuilder.AddColumn<DateTime>("MissingReturnedAt", "LostFoundReports", type: "datetime(6)", nullable: true);

        migrationBuilder.Sql("""
            WITH RECURSIVE
            links AS (
                SELECT LostReportId AS ReportId, FoundReportId AS LinkedReportId
                FROM MatchRecords WHERE IsConfirmed = TRUE
                UNION ALL
                SELECT FoundReportId, LostReportId
                FROM MatchRecords WHERE IsConfirmed = TRUE
            ),
            completed AS (
                SELECT c.ReportId, MIN(s.CompletedAt) AS ReturnDate
                FROM ClaimRecords c
                JOIN ClaimSchedules s ON s.ClaimId = c.ClaimId
                WHERE c.Status = 'Returned' AND s.Status = 'Completed' AND s.CompletedAt IS NOT NULL
                GROUP BY c.ReportId
            ),
            connected AS (
                SELECT ReportId AS ReturnReportId, ReportId AS MemberId FROM completed
                UNION DISTINCT
                SELECT c.ReturnReportId, l.LinkedReportId
                FROM connected c JOIN links l ON l.ReportId = c.MemberId
            ),
            totals AS (
                SELECT c.ReturnReportId, MIN(r.CreatedAt) AS OriginalSubmission
                FROM connected c JOIN LostFoundReports r ON r.ReportId = c.MemberId
                GROUP BY c.ReturnReportId
            ),
            per_report AS (
                SELECT c.MemberId, d.ReturnDate, t.OriginalSubmission,
                    ROW_NUMBER() OVER (PARTITION BY c.MemberId ORDER BY d.ReturnDate, c.ReturnReportId) AS Position
                FROM connected c
                JOIN completed d ON d.ReportId = c.ReturnReportId
                JOIN totals t ON t.ReturnReportId = c.ReturnReportId
            )
            UPDATE LostFoundReports r JOIN per_report p ON p.MemberId = r.ReportId AND p.Position = 1
            SET r.MissingReturnedAt = p.ReturnDate,
                r.FinalMissingDays = GREATEST(0, DATEDIFF(p.ReturnDate, p.OriginalSubmission))
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("FinalMissingDays", "LostFoundReports");
        migrationBuilder.DropColumn("MissingReturnedAt", "LostFoundReports");
    }
}
