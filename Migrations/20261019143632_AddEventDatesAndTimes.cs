using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CDM_OneServe_API.Migrations
{
    /// <inheritdoc />
    public partial class AddEventDatesAndTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EndTime",
                table: "Events",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EventDatesJson",
                table: "Events",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "StartTime",
                table: "Events",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "EventDatesJson",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Events");
        }
    }
}
