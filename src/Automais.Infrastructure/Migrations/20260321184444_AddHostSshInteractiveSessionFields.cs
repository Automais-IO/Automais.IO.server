using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostSshInteractiveSessionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSshInteractiveReportAt",
                schema: "public",
                table: "hosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SshInteractiveSessionOpen",
                schema: "public",
                table: "hosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SshInteractiveSessionSince",
                schema: "public",
                table: "hosts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSshInteractiveReportAt",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "SshInteractiveSessionOpen",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "SshInteractiveSessionSince",
                schema: "public",
                table: "hosts");
        }
    }
}
