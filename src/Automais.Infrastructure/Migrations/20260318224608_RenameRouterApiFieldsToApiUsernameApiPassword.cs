using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameRouterApiFieldsToApiUsernameApiPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RouterOsApiUsername",
                schema: "public",
                table: "routers",
                newName: "ApiUsername");

            migrationBuilder.RenameColumn(
                name: "RouterOsApiPassword",
                schema: "public",
                table: "routers",
                newName: "ApiPasswordTemporaria");

            migrationBuilder.RenameColumn(
                name: "AutomaisApiPassword",
                schema: "public",
                table: "routers",
                newName: "ApiPassword");

            migrationBuilder.AlterColumn<string>(
                name: "ApiPassword",
                schema: "public",
                table: "routers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ApiUsername",
                schema: "public",
                table: "routers",
                newName: "RouterOsApiUsername");

            migrationBuilder.RenameColumn(
                name: "ApiPasswordTemporaria",
                schema: "public",
                table: "routers",
                newName: "RouterOsApiPassword");

            migrationBuilder.RenameColumn(
                name: "ApiPassword",
                schema: "public",
                table: "routers",
                newName: "AutomaisApiPassword");

            migrationBuilder.AlterColumn<string>(
                name: "AutomaisApiPassword",
                schema: "public",
                table: "routers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }
    }
}
