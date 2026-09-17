using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoricalPriceBars",
                columns: table => new
                {
                    HistoricalPriceBarId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Open = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    High = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Low = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Close = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    Volume = table.Column<long>(type: "INTEGER", nullable: true),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalPriceBars", x => x.HistoricalPriceBarId);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalPriceCoverages",
                columns: table => new
                {
                    HistoricalPriceCoverageId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalPriceCoverages", x => x.HistoricalPriceCoverageId);
                });

            migrationBuilder.CreateTable(
                name: "MarketQuoteSnapshots",
                columns: table => new
                {
                    MarketQuoteSnapshotId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TimestampUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Last = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Bid = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Ask = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Open = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    High = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Low = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    PreviousClose = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Volume = table.Column<long>(type: "INTEGER", nullable: true),
                    IsDelayed = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketQuoteSnapshots", x => x.MarketQuoteSnapshotId);
                });

            migrationBuilder.CreateTable(
                name: "OptionContractSnapshots",
                columns: table => new
                {
                    OptionContractSnapshotId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OptionSymbol = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UnderlyingSymbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    TimestampUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Expiration = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Strike = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false),
                    OptionType = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Bid = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Ask = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Last = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    Volume = table.Column<long>(type: "INTEGER", nullable: true),
                    OpenInterest = table.Column<long>(type: "INTEGER", nullable: true),
                    ImpliedVolatility = table.Column<double>(type: "REAL", nullable: true),
                    Delta = table.Column<double>(type: "REAL", nullable: true),
                    Gamma = table.Column<double>(type: "REAL", nullable: true),
                    Theta = table.Column<double>(type: "REAL", nullable: true),
                    Vega = table.Column<double>(type: "REAL", nullable: true),
                    UnderlyingPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionContractSnapshots", x => x.OptionContractSnapshotId);
                });

            migrationBuilder.CreateTable(
                name: "OptionExpirationCaches",
                columns: table => new
                {
                    OptionExpirationCacheId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpirationsJson = table.Column<string>(type: "TEXT", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionExpirationCaches", x => x.OptionExpirationCacheId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalPriceBars_Symbol_Date_Provider",
                table: "HistoricalPriceBars",
                columns: new[] { "Symbol", "Date", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalPriceBars_Symbol_Provider_Date",
                table: "HistoricalPriceBars",
                columns: new[] { "Symbol", "Provider", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalPriceCoverages_Symbol_Provider_StartDate_EndDate",
                table: "HistoricalPriceCoverages",
                columns: new[] { "Symbol", "Provider", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_MarketQuoteSnapshots_Symbol_Provider_Timestamp",
                table: "MarketQuoteSnapshots",
                columns: new[] { "Symbol", "Provider", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_OptionContractSnapshots_OptionSymbol_Provider_Timestamp",
                table: "OptionContractSnapshots",
                columns: new[] { "OptionSymbol", "Provider", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_OptionContractSnapshots_UnderlyingSymbol_Provider_Timestamp",
                table: "OptionContractSnapshots",
                columns: new[] { "UnderlyingSymbol", "Provider", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_OptionExpirationCaches_Symbol_Provider_RetrievedAt",
                table: "OptionExpirationCaches",
                columns: new[] { "Symbol", "Provider", "RetrievedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalPriceBars");

            migrationBuilder.DropTable(
                name: "HistoricalPriceCoverages");

            migrationBuilder.DropTable(
                name: "MarketQuoteSnapshots");

            migrationBuilder.DropTable(
                name: "OptionContractSnapshots");

            migrationBuilder.DropTable(
                name: "OptionExpirationCaches");
        }
    }
}
