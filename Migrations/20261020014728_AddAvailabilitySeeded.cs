using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CDM_OneServe_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAvailabilitySeeded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_guidance_availability_CounselorId",
                table: "guidance_availability");

            migrationBuilder.AddColumn<bool>(
                name: "AvailabilitySeeded",
                table: "guidance_counselor_profiles",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_guidance_availability_CounselorId_DayOfWeek_StartTime_EndTime",
                table: "guidance_availability",
                columns: new[] { "CounselorId", "DayOfWeek", "StartTime", "EndTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_guidance_availability_CounselorId_DayOfWeek_StartTime_EndTime",
                table: "guidance_availability");

            migrationBuilder.DropColumn(
                name: "AvailabilitySeeded",
                table: "guidance_counselor_profiles");

            migrationBuilder.CreateIndex(
                name: "IX_guidance_availability_CounselorId",
                table: "guidance_availability",
                column: "CounselorId");
        }
    }
}
