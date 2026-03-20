using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHostPeersAndVpnIpAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastMetricsAt",
                schema: "public",
                table: "hosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetricsJson",
                schema: "public",
                table: "hosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningStatus",
                schema: "public",
                table: "hosts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SshPrivateKey",
                schema: "public",
                table: "hosts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SshPublicKey",
                schema: "public",
                table: "hosts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "host_wireguard_peers",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HostId = table.Column<Guid>(type: "uuid", nullable: false),
                    VpnNetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PrivateKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PeerIp = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastHandshake = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BytesReceived = table.Column<long>(type: "bigint", nullable: true),
                    BytesSent = table.Column<long>(type: "bigint", nullable: true),
                    PingSuccess = table.Column<bool>(type: "boolean", nullable: true),
                    PingAvgTimeMs = table.Column<double>(type: "double precision", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigContent = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    ResourceKind = table.Column<string>(type: "text", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_wireguard_peers",
                schema: "public");

            migrationBuilder.DropTable(
                name: "vpn_ip_allocations",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "LastMetricsAt",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "MetricsJson",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "ProvisioningStatus",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "SshPrivateKey",
                schema: "public",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "SshPublicKey",
                schema: "public",
                table: "hosts");
        }
    }
}
