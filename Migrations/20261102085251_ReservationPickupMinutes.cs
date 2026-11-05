using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CDM_OneServe_API.Migrations
{
    /// <inheritdoc />
    public partial class ReservationPickupMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReservationPickupDays",
                table: "librarysettings",
                newName: "ReservationPickupMinutes");

            // The old column held days (1-14). Keep the new 15-minute default
            // instead of reinterpreting those values as minutes.
            migrationBuilder.Sql(
                "UPDATE librarysettings SET ReservationPickupMinutes = 15 WHERE ReservationPickupMinutes <= 14;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ReservationPickupMinutes",
                table: "librarysettings",
                newName: "ReservationPickupDays");

            // Restore a safe default because minute-based values
            // cannot be accurately converted back to their original day values.
            migrationBuilder.Sql(
                "UPDATE librarysettings SET ReservationPickupDays = 3;");
        }
    }
}
