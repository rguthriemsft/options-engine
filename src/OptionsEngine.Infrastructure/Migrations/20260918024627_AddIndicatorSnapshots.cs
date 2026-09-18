using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIndicatorSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmptyOptionChainSnapshots",
                columns: table => new
                {
                    EmptyOptionChainSnapshotId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UnderlyingSymbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Expiration = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TimestampUtcTicks = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmptyOptionChainSnapshots", x => x.EmptyOptionChainSnapshotId);
                });

            migrationBuilder.CreateTable(
                name: "IndicatorSnapshots",
                columns: table => new
                {
                    IndicatorSnapshotId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    IndicatorCalculationVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConfigurationVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Iv30Value = table.Column<double>(type: "REAL", nullable: true),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicatorSnapshots", x => x.IndicatorSnapshotId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmptyOptionChainSnapshots_UnderlyingSymbol_Provider_Expiration_TimestampUtcTicks",
                table: "EmptyOptionChainSnapshots",
                columns: new[] { "UnderlyingSymbol", "Provider", "Expiration", "TimestampUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorSnapshots_Symbol_AsOfDate_IndicatorCalculationVersion_ConfigurationVersion",
                table: "IndicatorSnapshots",
                columns: new[] { "Symbol", "AsOfDate", "IndicatorCalculationVersion", "ConfigurationVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmptyOptionChainSnapshots");

            migrationBuilder.DropTable(
                name: "IndicatorSnapshots");
        }
    }
}
