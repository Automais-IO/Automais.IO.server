using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TenantUserEmailDeliveryFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailDeliveryFailedAt",
                schema: "public",
                table: "tenant_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailDeliveryFailureMessage",
                schema: "public",
                table: "tenant_users",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailDeliveryFailedAt",
                schema: "public",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "EmailDeliveryFailureMessage",
                schema: "public",
                table: "tenant_users");
        }
    }
}
