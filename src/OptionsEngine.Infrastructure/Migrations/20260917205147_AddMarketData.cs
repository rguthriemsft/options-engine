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
            migrationBuilder.AddColumn<long>(
                name: "TimestampUtcTicks",
                table: "OptionContractSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "TimestampUtcTicks",
                table: "MarketQuoteSnapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

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

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalPriceCoverages_Symbol_Provider_StartDate_EndDate",
                table: "HistoricalPriceCoverages",
                columns: new[] { "Symbol", "Provider", "StartDate", "EndDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalPriceCoverages");

            migrationBuilder.DropColumn(
                name: "TimestampUtcTicks",
                table: "OptionContractSnapshots");

            migrationBuilder.DropColumn(
                name: "TimestampUtcTicks",
                table: "MarketQuoteSnapshots");
        }
    }
}
