using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RouterOsApiAuthStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RouterOsApiAuthCheckedAt",
                schema: "public",
                table: "routers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RouterOsApiAuthMessage",
                schema: "public",
                table: "routers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouterOsApiAuthStatus",
                schema: "public",
                table: "routers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RouterOsApiAuthCheckedAt",
                schema: "public",
                table: "routers");

            migrationBuilder.DropColumn(
                name: "RouterOsApiAuthMessage",
                schema: "public",
                table: "routers");

            migrationBuilder.DropColumn(
                name: "RouterOsApiAuthStatus",
                schema: "public",
                table: "routers");
        }
    }
}
