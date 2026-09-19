using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryStrategyEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntryStrategyEvaluations",
                columns: table => new
                {
                    EntryStrategyEvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HoldingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IndicatorAsOfDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EvaluationTimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IndicatorCalculationVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfigurationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    StrategyVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CcosStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CcosScore = table.Column<double>(type: "REAL", nullable: true),
                    CcosClassification = table.Column<string>(type: "TEXT", nullable: true),
                    EntryCandidateExists = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferredInitialOptionSymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PreferredInitialStrike = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    PreferredInitialExpiration = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PreferredInitialReferencePremium = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    DispositionReason = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EvaluationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntryStrategyEvaluations", x => x.EntryStrategyEvaluationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntryStrategyEvaluations_HoldingId_CalculatedAtUtc",
                table: "EntryStrategyEvaluations",
                columns: new[] { "HoldingId", "CalculatedAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_EntryStrategyEvaluations_Symbol",
                table: "EntryStrategyEvaluations",
                column: "Symbol");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntryStrategyEvaluations");
        }
    }
}
