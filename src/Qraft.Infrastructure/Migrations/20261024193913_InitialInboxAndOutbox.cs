using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qraft.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialInboxAndOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceivedInspections",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: false),
                    SimulationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchCode = table.Column<string>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivedInspections", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "ResultOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResultOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SampleAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    IsConformant = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriteriaJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleAssessments_ReceivedInspections_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ReceivedInspections",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivedInspections_ReceivedAtUtc",
                table: "ReceivedInspections",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ResultOutbox_PublishedAtUtc_NextAttemptAtUtc",
                table: "ResultOutbox",
                columns: new[] { "PublishedAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ResultOutbox_SourceMessageId",
                table: "ResultOutbox",
                column: "SourceMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SampleAssessments_MessageId_Code",
                table: "SampleAssessments",
                columns: new[] { "MessageId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResultOutbox");

            migrationBuilder.DropTable(
                name: "SampleAssessments");

            migrationBuilder.DropTable(
                name: "ReceivedInspections");
        }
    }
}
