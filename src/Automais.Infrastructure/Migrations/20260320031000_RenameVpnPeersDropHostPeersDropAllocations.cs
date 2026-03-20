using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameVpnPeersDropHostPeersDropAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_router_wireguard_peers_routers_RouterId",
                schema: "public",
                table: "router_wireguard_peers");

            migrationBuilder.DropForeignKey(
                name: "FK_router_wireguard_peers_vpn_networks_VpnNetworkId",
                schema: "public",
                table: "router_wireguard_peers");

            migrationBuilder.DropTable(
                name: "host_wireguard_peers",
                schema: "public");

            migrationBuilder.DropTable(
                name: "vpn_ip_allocations",
                schema: "public");

            migrationBuilder.DropPrimaryKey(
                name: "PK_router_wireguard_peers",
                schema: "public",
                table: "router_wireguard_peers");

            migrationBuilder.RenameTable(
                name: "router_wireguard_peers",
                schema: "public",
                newName: "vpn_peers",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_router_wireguard_peers_VpnNetworkId",
                schema: "public",
                table: "vpn_peers",
                newName: "IX_vpn_peers_VpnNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_router_wireguard_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "vpn_peers",
                newName: "IX_vpn_peers_RouterId_VpnNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_router_wireguard_peers_RouterId",
                schema: "public",
                table: "vpn_peers",
                newName: "IX_vpn_peers_RouterId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_vpn_peers",
                schema: "public",
                table: "vpn_peers",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_vpn_peers_routers_RouterId",
                schema: "public",
                table: "vpn_peers",
                column: "RouterId",
                principalSchema: "public",
                principalTable: "routers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_vpn_peers_vpn_networks_VpnNetworkId",
                schema: "public",
                table: "vpn_peers",
                column: "VpnNetworkId",
                principalSchema: "public",
                principalTable: "vpn_networks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_vpn_peers_routers_RouterId",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.DropForeignKey(
                name: "FK_vpn_peers_vpn_networks_VpnNetworkId",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_vpn_peers",
                schema: "public",
                table: "vpn_peers");

            migrationBuilder.RenameTable(
                name: "vpn_peers",
                schema: "public",
                newName: "router_wireguard_peers",
                newSchema: "public");

            migrationBuilder.RenameIndex(
                name: "IX_vpn_peers_VpnNetworkId",
                schema: "public",
                table: "router_wireguard_peers",
                newName: "IX_router_wireguard_peers_VpnNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_vpn_peers_RouterId_VpnNetworkId",
                schema: "public",
                table: "router_wireguard_peers",
                newName: "IX_router_wireguard_peers_RouterId_VpnNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_vpn_peers_RouterId",
                schema: "public",
                table: "router_wireguard_peers",
                newName: "IX_router_wireguard_peers_RouterId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_router_wireguard_peers",
                schema: "public",
                table: "router_wireguard_peers",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "host_wireguard_peers",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<Guid>(type: "uuid", nullable: false),
                    VpnNetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    BytesReceived = table.Column<long>(type: "bigint", nullable: true),
                    BytesSent = table.Column<long>(type: "bigint", nullable: true),
                    ConfigContent = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastHandshake = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PeerIp = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PingAvgTimeMs = table.Column<double>(type: "double precision", nullable: true),
                    PingSuccess = table.Column<bool>(type: "boolean", nullable: true),
                    PrivateKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_wireguard_peers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_host_wireguard_peers_hosts_HostId",
                        column: x => x.HostId,
                        principalSchema: "public",
                        principalTable: "hosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_host_wireguard_peers_vpn_networks_VpnNetworkId",
                        column: x => x.VpnNetworkId,
                        principalSchema: "public",
                        principalTable: "vpn_networks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vpn_ip_allocations",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VpnNetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResourceKind = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vpn_ip_allocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vpn_ip_allocations_vpn_networks_VpnNetworkId",
                        column: x => x.VpnNetworkId,
                        principalSchema: "public",
                        principalTable: "vpn_networks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_host_wireguard_peers_HostId",
                schema: "public",
                table: "host_wireguard_peers",
                column: "HostId");

            migrationBuilder.CreateIndex(
                name: "IX_host_wireguard_peers_HostId_VpnNetworkId",
                schema: "public",
                table: "host_wireguard_peers",
                columns: new[] { "HostId", "VpnNetworkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_host_wireguard_peers_VpnNetworkId",
                schema: "public",
                table: "host_wireguard_peers",
                column: "VpnNetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_vpn_ip_allocations_ResourceId",
                schema: "public",
                table: "vpn_ip_allocations",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_vpn_ip_allocations_VpnNetworkId",
                schema: "public",
                table: "vpn_ip_allocations",
                column: "VpnNetworkId");

            migrationBuilder.CreateIndex(
                name: "IX_vpn_ip_allocations_VpnNetworkId_Address",
                schema: "public",
                table: "vpn_ip_allocations",
                columns: new[] { "VpnNetworkId", "Address" },
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.AddForeignKey(
                name: "FK_router_wireguard_peers_routers_RouterId",
                schema: "public",
                table: "router_wireguard_peers",
                column: "RouterId",
                principalSchema: "public",
                principalTable: "routers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_router_wireguard_peers_vpn_networks_VpnNetworkId",
                schema: "public",
                table: "router_wireguard_peers",
                column: "VpnNetworkId",
                principalSchema: "public",
                principalTable: "vpn_networks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
