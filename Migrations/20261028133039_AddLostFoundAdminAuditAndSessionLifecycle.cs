using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CDM_OneServe_API.Migrations
{
    /// <inheritdoc />
    public partial class AddLostFoundAdminAuditAndSessionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "UserSessions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LoggedOutAt",
                table: "UserSessions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoutReason",
                table: "UserSessions",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "LostFoundReports",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByName",
                table: "LostFoundReports",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReviewedByRole",
                table: "LostFoundReports",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "ReviewedByUserId",
                table: "LostFoundReports",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByName",
                table: "ClaimSchedules",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByRole",
                table: "ClaimSchedules",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DecisionByName",
                table: "ClaimRecords",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "DecisionByRole",
                table: "ClaimRecords",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ActorName",
                table: "admin_activity_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ActorRole",
                table: "admin_activity_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "admin_activity_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "EntityId",
                table: "admin_activity_logs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "admin_activity_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "admin_activity_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "NewStatus",
                table: "admin_activity_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PreviousStatus",
                table: "admin_activity_logs",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // The existing Lost & Found module owner becomes the Super Admin.
            // LostFoundAdmin remains a recognised alias in code, so this is a
            // rename in the data, not a lockout.
            migrationBuilder.Sql(
                "UPDATE `Users` SET `Role` = 'LostFoundSuperAdmin' WHERE `Role` = 'LostFoundAdmin';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "LoggedOutAt",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "LogoutReason",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "LostFoundReports");

            migrationBuilder.DropColumn(
                name: "ReviewedByName",
                table: "LostFoundReports");

            migrationBuilder.DropColumn(
                name: "ReviewedByRole",
                table: "LostFoundReports");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "LostFoundReports");

            migrationBuilder.DropColumn(
                name: "VerifiedByName",
                table: "ClaimSchedules");

            migrationBuilder.DropColumn(
                name: "VerifiedByRole",
                table: "ClaimSchedules");

            migrationBuilder.DropColumn(
                name: "DecisionByName",
                table: "ClaimRecords");

            migrationBuilder.DropColumn(
                name: "DecisionByRole",
                table: "ClaimRecords");

            migrationBuilder.DropColumn(
                name: "ActorName",
                table: "admin_activity_logs");

            migrationBuilder.DropColumn(
                name: "ActorRole",
                table: "admin_activity_logs");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "admin_activity_logs");

            migrationBuilder.DropColumn(
                name: "EntityId",
                table: "admin_activity_logs");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "admin_activity_logs");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "admin_activity_logs");

            migrationBuilder.DropColumn(
                name: "NewStatus",
                table: "admin_activity_logs");

            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                table: "admin_activity_logs");
        }
    }
}
