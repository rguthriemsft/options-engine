using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistDefenseEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefenseEvaluations",
                columns: table => new
                {
                    DefenseEvaluationEntityId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DefenseEvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OpenShortCallPositionId = table.Column<long>(type: "INTEGER", nullable: false),
                    HoldingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    OptionSymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DefenseEvaluationTimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Drs = table.Column<double>(type: "REAL", nullable: true),
                    DrsClassification = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ProfitTakingSignal = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    HardDefenseStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RollEngineRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    RollEvaluationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Disposition = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CurrentCcos = table.Column<double>(type: "REAL", nullable: true),
                    ConfigurationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    DefenseStrategyVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RollStrategyVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EvaluationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefenseEvaluations", x => x.DefenseEvaluationEntityId);
                    table.UniqueConstraint("AK_DefenseEvaluations_DefenseEvaluationId", x => x.DefenseEvaluationId);
                });

            migrationBuilder.CreateTable(
                name: "RollEvaluations",
                columns: table => new
                {
                    RollEvaluationEntityId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RollEvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefenseEvaluationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefenseEvaluationTimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CalculatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CurrentCcos = table.Column<double>(type: "REAL", nullable: true),
                    RankableCandidateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    InsufficientCandidateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredOptionSymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PreferredStrike = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    PreferredExpiration = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PreferredRqs = table.Column<double>(type: "REAL", nullable: true),
                    ConfigurationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    RollStrategyVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EvaluationJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollEvaluations", x => x.RollEvaluationEntityId);
                    table.ForeignKey(
                        name: "FK_RollEvaluations_DefenseEvaluations_DefenseEvaluationId",
                        column: x => x.DefenseEvaluationId,
                        principalTable: "DefenseEvaluations",
                        principalColumn: "DefenseEvaluationId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefenseEvaluations_HoldingId_OpenShortCallPositionId_CalculatedAtUtc_DefenseEvaluationEntityId",
                table: "DefenseEvaluations",
                columns: new[] { "HoldingId", "OpenShortCallPositionId", "CalculatedAtUtc", "DefenseEvaluationEntityId" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_DefenseEvaluations_RollEvaluationId",
                table: "DefenseEvaluations",
                column: "RollEvaluationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RollEvaluations_DefenseEvaluationId",
                table: "RollEvaluations",
                column: "DefenseEvaluationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RollEvaluations_RollEvaluationId",
                table: "RollEvaluations",
                column: "RollEvaluationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RollEvaluations");

            migrationBuilder.DropTable(
                name: "DefenseEvaluations");
        }
    }
}
