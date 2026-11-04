using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Qraft.Simulator.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScenarioFixtures",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedStatus = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedOutcome = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScenarioFixtures", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "SimulationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioCode = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulationRuns_ScenarioFixtures_ScenarioCode",
                        column: x => x.ScenarioCode,
                        principalTable: "ScenarioFixtures",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourceMessages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchCode = table.Column<string>(type: "TEXT", nullable: false),
                    Stage = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResultId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceMessages", x => x.MessageId);
                    table.ForeignKey(
                        name: "FK_SourceMessages_SimulationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "SimulationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmissionOutbox_SourceMessages_SourceMessageId",
                        column: x => x.SourceMessageId,
                        principalTable: "SourceMessages",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_CreatedAt",
                table: "SimulationRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_ScenarioCode",
                table: "SimulationRuns",
                column: "ScenarioCode");

            migrationBuilder.CreateIndex(
                name: "IX_SourceMessages_ResultId",
                table: "SourceMessages",
                column: "ResultId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceMessages_RunId",
                table: "SourceMessages",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionOutbox_PublishedAt_NextAttemptAt",
                table: "SubmissionOutbox",
                columns: new[] { "PublishedAt", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionOutbox_SourceMessageId",
                table: "SubmissionOutbox",
                column: "SourceMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubmissionOutbox");

            migrationBuilder.DropTable(
                name: "SourceMessages");

            migrationBuilder.DropTable(
                name: "SimulationRuns");

            migrationBuilder.DropTable(
                name: "ScenarioFixtures");
        }
    }
}
