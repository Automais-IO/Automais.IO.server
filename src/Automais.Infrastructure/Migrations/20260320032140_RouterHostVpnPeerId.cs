using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RouterHostVpnPeerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vpn_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.AlterColumn<Guid>(
                name: "RouterId",
                schema: "public",
                table: "vpn_peers",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "VpnPeerId",
                schema: "public",
                table: "routers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VpnPeerId",
                schema: "public",
                table: "hosts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vpn_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "vpn_peers",
                columns: new[] { "RouterId", "VpnNetworkId" },
                unique: true,
                filter: "\"RouterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_routers_VpnPeerId",
                schema: "public",
                table: "routers",
                column: "VpnPeerId");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_VpnPeerId",
                schema: "public",
                table: "hosts",
                column: "VpnPeerId");

            migrationBuilder.AddForeignKey(
                name: "FK_hosts_vpn_peers_VpnPeerId",
                schema: "public",
                table: "hosts",
                column: "VpnPeerId",
                principalSchema: "public",
                principalTable: "vpn_peers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_routers_vpn_peers_VpnPeerId",
                schema: "public",
                table: "routers",
                column: "VpnPeerId",
                principalSchema: "public",
                principalTable: "vpn_peers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Preenche VpnPeerId dos routers já existentes (peer da mesma rede VPN).
            migrationBuilder.Sql("""
                UPDATE routers r
                SET "VpnPeerId" = p."Id"
                FROM vpn_peers p
                WHERE r."VpnPeerId" IS NULL
                  AND p."RouterId" = r."Id"
                  AND r."VpnNetworkId" IS NOT NULL
                  AND p."VpnNetworkId" = r."VpnNetworkId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_hosts_vpn_peers_VpnPeerId",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropForeignKey(
                name: "FK_routers_vpn_peers_VpnPeerId",
                schema: "public",
                table: "routers");

            migrationBuilder.DropIndex(
                name: "IX_vpn_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.DropIndex(
                name: "IX_routers_VpnPeerId",
                schema: "public",
                table: "routers");

            migrationBuilder.DropIndex(
                name: "IX_hosts_VpnPeerId",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "VpnPeerId",
                schema: "public",
                table: "routers");

            migrationBuilder.DropColumn(
                name: "VpnPeerId",
                schema: "public",
                table: "hosts");

            migrationBuilder.AlterColumn<Guid>(
                name: "RouterId",
                schema: "public",
                table: "vpn_peers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vpn_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "vpn_peers",
                columns: new[] { "RouterId", "VpnNetworkId" },
                unique: true);
        }
    }
}
