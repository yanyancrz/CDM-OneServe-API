using System;
using CDM_OneServe_API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CDM_OneServe_API.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261019005300_LibraryNoFinesSemesterBans")]
    public partial class LibraryNoFinesSemesterBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverdueFinePerDay",
                table: "librarysettings");

            migrationBuilder.DropColumn(
                name: "Fine",
                table: "borrowtransactions");

            migrationBuilder.DropColumn(
                name: "FinePaymentStatus",
                table: "borrowtransactions");

            migrationBuilder.AddColumn<DateTime>(
                name: "BorrowBanUntil",
                table: "Users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSemEnd",
                table: "librarysettings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstSemStart",
                table: "librarysettings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SecondSemEnd",
                table: "librarysettings",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SecondSemStart",
                table: "librarysettings",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BorrowBanUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FirstSemEnd",
                table: "librarysettings");

            migrationBuilder.DropColumn(
                name: "FirstSemStart",
                table: "librarysettings");

            migrationBuilder.DropColumn(
                name: "SecondSemEnd",
                table: "librarysettings");

            migrationBuilder.DropColumn(
                name: "SecondSemStart",
                table: "librarysettings");

            migrationBuilder.AddColumn<decimal>(
                name: "OverdueFinePerDay",
                table: "librarysettings",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Fine",
                table: "borrowtransactions",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FinePaymentStatus",
                table: "borrowtransactions",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);
        }
    }
}
