using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostRemoteDisplay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemoteDisplayEnabled",
                schema: "public",
                table: "hosts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "RemoteDisplayPort",
                schema: "public",
                table: "hosts",
                type: "integer",
                nullable: false,
                defaultValue: 5900);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteDisplayEnabled",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "RemoteDisplayPort",
                schema: "public",
                table: "hosts");
        }
    }
}
