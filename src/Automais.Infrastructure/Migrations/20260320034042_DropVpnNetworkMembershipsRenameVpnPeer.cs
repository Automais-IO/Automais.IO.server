using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropVpnNetworkMembershipsRenameVpnPeer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vpn_network_memberships",
                schema: "public");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vpn_network_memberships",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VpnNetworkId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedIp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vpn_network_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vpn_network_memberships_tenant_users_TenantUserId",
                        column: x => x.TenantUserId,
                        principalSchema: "public",
                        principalTable: "tenant_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vpn_network_memberships_vpn_networks_VpnNetworkId",
                        column: x => x.VpnNetworkId,
                        principalSchema: "public",
                        principalTable: "vpn_networks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vpn_network_memberships_TenantUserId",
                schema: "public",
                table: "vpn_network_memberships",
                column: "TenantUserId");

            migrationBuilder.CreateIndex(
                name: "IX_vpn_network_memberships_VpnNetworkId_TenantUserId",
                schema: "public",
                table: "vpn_network_memberships",
                columns: new[] { "VpnNetworkId", "TenantUserId" },
                unique: true);
        }
    }
}
