using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceWebDeviceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DevEui",
                schema: "public",
                table: "devices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "public",
                table: "devices",
                type: "text",
                nullable: false,
                defaultValue: "LoRaWan");

            migrationBuilder.AddColumn<bool>(
                name: "WebDeviceEnabled",
                schema: "public",
                table: "devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WebDeviceTokenHash",
                schema: "public",
                table: "devices",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "public",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "WebDeviceEnabled",
                schema: "public",
                table: "devices");

            migrationBuilder.DropColumn(
                name: "WebDeviceTokenHash",
                schema: "public",
                table: "devices");

            migrationBuilder.AlterColumn<string>(
                name: "DevEui",
                schema: "public",
                table: "devices",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);
        }
    }
}
