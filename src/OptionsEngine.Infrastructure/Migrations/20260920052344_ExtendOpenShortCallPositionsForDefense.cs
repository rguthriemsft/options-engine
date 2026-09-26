using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptionsEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExtendOpenShortCallPositionsForDefense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OpenedAtUtc",
                table: "OpenShortCallPositions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningPremiumPerShare",
                table: "OpenShortCallPositions",
                type: "TEXT",
                precision: 18,
                scale: 6,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpenedAtUtc",
                table: "OpenShortCallPositions");

            migrationBuilder.DropColumn(
                name: "OpeningPremiumPerShare",
                table: "OpenShortCallPositions");
        }
    }
}
