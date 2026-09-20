using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPositionSizing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpenShortCallPositions",
                columns: table => new
                {
                    OpenShortCallPositionId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HoldingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OptionSymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Contracts = table.Column<int>(type: "INTEGER", nullable: false),
                    Strike = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Expiration = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpenShortCallPositions", x => x.OpenShortCallPositionId);
                });

            migrationBuilder.CreateTable(
                name: "PositionSizingEvaluations",
                columns: table => new
                {
                    PositionSizingEvaluationEntityId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PositionSizingEvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntryStrategyEvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HoldingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SizingTimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfigurationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionSizingStrategyVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DesiredTotalContracts = table.Column<int>(type: "INTEGER", nullable: true),
                    AdditionalContracts = table.Column<int>(type: "INTEGER", nullable: true),
                    ResultingTotalContracts = table.Column<int>(type: "INTEGER", nullable: true),
                    ExistingDer = table.Column<double>(type: "REAL", nullable: true),
                    MaximumDer = table.Column<double>(type: "REAL", nullable: true),
                    EvaluationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionSizingEvaluations", x => x.PositionSizingEvaluationEntityId);
                    table.ForeignKey(
                        name: "FK_PositionSizingEvaluations_EntryStrategyEvaluations_EntryStrategyEvaluationId",
                        column: x => x.EntryStrategyEvaluationId,
                        principalTable: "EntryStrategyEvaluations",
                        principalColumn: "EntryStrategyEvaluationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpenShortCallPositions_HoldingId_OptionSymbol",
                table: "OpenShortCallPositions",
                columns: new[] { "HoldingId", "OptionSymbol" });

            migrationBuilder.CreateIndex(
                name: "IX_PositionSizingEvaluations_EntryStrategyEvaluationId_CalculatedAtUtc",
                table: "PositionSizingEvaluations",
                columns: new[] { "EntryStrategyEvaluationId", "CalculatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PositionSizingEvaluations_PositionSizingEvaluationId",
                table: "PositionSizingEvaluations",
                column: "PositionSizingEvaluationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpenShortCallPositions");

            migrationBuilder.DropTable(
                name: "PositionSizingEvaluations");
        }
    }
}
