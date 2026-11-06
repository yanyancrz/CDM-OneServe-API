using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CDM_OneServe_API.Migrations
{
    /// <inheritdoc />
    public partial class GuidanceNarrativeReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing databases differ: some have IX_guidance_counseling_records_CounselorId
            // / StudentId, others already use differently-named indexes (e.g. the
            // restored production schema uses ix_gcr_counselor / ix_gcr_student).
            // Dropping them is not required - the new composite indexes below are
            // additive and the old single-column indexes remain harmless.

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "guidance_counseling_records",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "AppointmentId",
                table: "guidance_counseling_records",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "guidance_counseling_records",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ArchivedBy",
                table: "guidance_counseling_records",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAt",
                table: "guidance_counseling_records",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FinalizedBy",
                table: "guidance_counseling_records",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FollowUpPlan",
                table: "guidance_counseling_records",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Interventions",
                table: "guidance_counseling_records",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "LastEditedBy",
                table: "guidance_counseling_records",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Observations",
                table: "guidance_counseling_records",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Recommendations",
                table: "guidance_counseling_records",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReportNumber",
                table: "guidance_counseling_records",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReportTitle",
                table: "guidance_counseling_records",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SessionObjectives",
                table: "guidance_counseling_records",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "guidance_counseling_records",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "guidance_narrative_attachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RecordId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StoredName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentType = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UploadedBy = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guidance_narrative_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_guidance_narrative_attachments_Users_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_guidance_narrative_attachments_guidance_counseling_records_R~",
                        column: x => x.RecordId,
                        principalTable: "guidance_counseling_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "guidance_narrative_report_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RecordId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    ActorRole = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Action = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Detail = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guidance_narrative_report_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_guidance_narrative_report_events_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_guidance_narrative_report_events_guidance_counseling_records~",
                        column: x => x.RecordId,
                        principalTable: "guidance_counseling_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_guidance_counseling_records_CounselorId_Status",
                table: "guidance_counseling_records",
                columns: new[] { "CounselorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_guidance_counseling_records_ReportNumber",
                table: "guidance_counseling_records",
                column: "ReportNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_guidance_counseling_records_StudentId_SessionDate",
                table: "guidance_counseling_records",
                columns: new[] { "StudentId", "SessionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_guidance_narrative_attachments_RecordId",
                table: "guidance_narrative_attachments",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_guidance_narrative_attachments_UploadedBy",
                table: "guidance_narrative_attachments",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_guidance_narrative_report_events_ActorId",
                table: "guidance_narrative_report_events",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_guidance_narrative_report_events_RecordId_CreatedAt",
                table: "guidance_narrative_report_events",
                columns: new[] { "RecordId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guidance_narrative_attachments");

            migrationBuilder.DropTable(
                name: "guidance_narrative_report_events");

            migrationBuilder.DropIndex(
                name: "IX_guidance_counseling_records_CounselorId_Status",
                table: "guidance_counseling_records");

            migrationBuilder.DropIndex(
                name: "IX_guidance_counseling_records_ReportNumber",
                table: "guidance_counseling_records");

            migrationBuilder.DropIndex(
                name: "IX_guidance_counseling_records_StudentId_SessionDate",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "ArchivedBy",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "FinalizedBy",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "FollowUpPlan",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "Interventions",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "LastEditedBy",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "Observations",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "Recommendations",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "ReportNumber",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "ReportTitle",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "SessionObjectives",
                table: "guidance_counseling_records");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "guidance_counseling_records");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "guidance_counseling_records",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<int>(
                name: "AppointmentId",
                table: "guidance_counseling_records",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
