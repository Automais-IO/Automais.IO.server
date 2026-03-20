using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRouterIdFromVpnPeers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Garantir VpnPeerId nos routers antes de remover RouterId dos peers.
            migrationBuilder.Sql("""
                UPDATE routers r
                SET "VpnPeerId" = p."Id"
                FROM vpn_peers p
                WHERE r."VpnPeerId" IS NULL
                  AND p."RouterId" IS NOT NULL
                  AND p."RouterId" = r."Id"
                  AND r."VpnNetworkId" IS NOT NULL
                  AND p."VpnNetworkId" = r."VpnNetworkId";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_vpn_peers_routers_RouterId",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.DropIndex(
                name: "IX_vpn_peers_RouterId",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.DropIndex(
                name: "IX_vpn_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.DropColumn(
                name: "RouterId",
                schema: "public",
                table: "vpn_peers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RouterId",
                schema: "public",
                table: "vpn_peers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vpn_peers_RouterId",
                schema: "public",
                table: "vpn_peers",
                column: "RouterId");

            migrationBuilder.CreateIndex(
                name: "IX_vpn_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "vpn_peers",
                columns: new[] { "RouterId", "VpnNetworkId" },
                unique: true,
                filter: "\"RouterId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_vpn_peers_routers_RouterId",
                schema: "public",
                table: "vpn_peers",
                column: "RouterId",
                principalSchema: "public",
                principalTable: "routers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.Sql("""
                UPDATE vpn_peers p
                SET "RouterId" = r."Id"
                FROM routers r
                WHERE r."VpnPeerId" = p."Id";
                """);
        }
    }
}
