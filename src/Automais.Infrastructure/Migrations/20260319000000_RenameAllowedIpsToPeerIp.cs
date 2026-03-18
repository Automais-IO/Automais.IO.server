using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameAllowedIpsToPeerIp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AllowedIps",
                schema: "public",
                table: "router_wireguard_peers",
                newName: "PeerIp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PeerIp",
                schema: "public",
                table: "router_wireguard_peers",
                newName: "AllowedIps");
        }
    }
}
