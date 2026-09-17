using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Broker = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsTaxDeferred = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.AccountId);
                });

            migrationBuilder.CreateTable(
                name: "Holdings",
                columns: table => new
                {
                    HoldingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    AssetType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Shares = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AssignmentSensitivity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TaxSensitivity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MaximumCoveragePercent = table.Column<decimal>(type: "TEXT", precision: 9, scale: 6, nullable: false),
                    MaximumInitialDelta = table.Column<double>(type: "REAL", nullable: false),
                    PreferredDeltaMinimum = table.Column<double>(type: "REAL", nullable: false),
                    PreferredDeltaMaximum = table.Column<double>(type: "REAL", nullable: false),
                    MinimumCcos = table.Column<double>(type: "REAL", nullable: false),
                    MinimumContractScore = table.Column<double>(type: "REAL", nullable: false),
                    MinimumPremium = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MinimumAnnualizedYield = table.Column<double>(type: "REAL", nullable: false),
                    MaximumDeltaExposureRatio = table.Column<double>(type: "REAL", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holdings", x => x.HoldingId);
                    table.ForeignKey(
                        name: "FK_Holdings_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaxLots",
                columns: table => new
                {
                    TaxLotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    HoldingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AcquisitionDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Shares = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CostBasisPerShare = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TotalCostBasis = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    HoldingPeriodClassification = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxLots", x => x.TaxLotId);
                    table.ForeignKey(
                        name: "FK_TaxLots_Holdings_HoldingId",
                        column: x => x.HoldingId,
                        principalTable: "Holdings",
                        principalColumn: "HoldingId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holdings_AccountId_Symbol",
                table: "Holdings",
                columns: new[] { "AccountId", "Symbol" });

            migrationBuilder.CreateIndex(
                name: "IX_TaxLots_HoldingId",
                table: "TaxLots",
                column: "HoldingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxLots");

            migrationBuilder.DropTable(
                name: "Holdings");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
