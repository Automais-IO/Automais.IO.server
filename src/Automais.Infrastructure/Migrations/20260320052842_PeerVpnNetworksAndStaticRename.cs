using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PeerVpnNetworksAndStaticRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_allowed_routes_router_allowed_networks_RouterAllowedNe~",
                schema: "public",
                table: "user_allowed_routes");

            migrationBuilder.DropForeignKey(
                name: "FK_router_allowed_networks_routers_RouterId",
                schema: "public",
                table: "router_allowed_networks");

            migrationBuilder.DropIndex(
                name: "IX_router_allowed_networks_RouterId_NetworkCidr",
                schema: "public",
                table: "router_allowed_networks");

            migrationBuilder.DropIndex(
                name: "IX_router_allowed_networks_RouterId",
                schema: "public",
                table: "router_allowed_networks");

            migrationBuilder.RenameTable(
                name: "router_allowed_networks",
                schema: "public",
                newName: "allowed_networks");

            migrationBuilder.AddColumn<Guid>(
                name: "VpnPeerId",
                schema: "public",
                table: "allowed_networks",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "allowed_networks" AS an
                SET "VpnPeerId" = r."VpnPeerId"
                FROM "routers" AS r
                WHERE r."Id" = an."RouterId" AND r."VpnPeerId" IS NOT NULL
                """);

            migrationBuilder.Sql(
                """DELETE FROM "allowed_networks" WHERE "VpnPeerId" IS NULL""");

            migrationBuilder.DropColumn(
                name: "RouterId",
                schema: "public",
                table: "allowed_networks");

            migrationBuilder.AlterColumn<Guid>(
                name: "VpnPeerId",
                schema: "public",
                table: "allowed_networks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_allowed_networks_VpnPeerId",
                schema: "public",
                table: "allowed_networks",
                column: "VpnPeerId");

            migrationBuilder.CreateIndex(
                name: "IX_allowed_networks_VpnPeerId_NetworkCidr",
                schema: "public",
                table: "allowed_networks",
                columns: new[] { "VpnPeerId", "NetworkCidr" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_allowed_networks_vpn_peers_VpnPeerId",
                schema: "public",
                table: "allowed_networks",
                column: "VpnPeerId",
                principalSchema: "public",
                principalTable: "vpn_peers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropForeignKey(
                name: "FK_router_static_routes_routers_RouterId",
                schema: "public",
                table: "router_static_routes");

            migrationBuilder.DropIndex(
                name: "IX_router_static_routes_RouterId_Destination",
                schema: "public",
                table: "router_static_routes");

            migrationBuilder.DropIndex(
                name: "IX_router_static_routes_RouterId",
                schema: "public",
                table: "router_static_routes");

            migrationBuilder.RenameTable(
                name: "router_static_routes",
                schema: "public",
                newName: "static_networks");

            migrationBuilder.AddColumn<Guid>(
                name: "VpnPeerId",
                schema: "public",
                table: "static_networks",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "static_networks" AS sn
                SET "VpnPeerId" = r."VpnPeerId"
                FROM "routers" AS r
                WHERE r."Id" = sn."RouterId" AND r."VpnPeerId" IS NOT NULL
                """);

            migrationBuilder.Sql(
                """DELETE FROM "static_networks" WHERE "VpnPeerId" IS NULL""");

            migrationBuilder.DropColumn(
                name: "RouterId",
                schema: "public",
                table: "static_networks");

            migrationBuilder.AlterColumn<Guid>(
                name: "VpnPeerId",
                schema: "public",
                table: "static_networks",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_static_networks_VpnPeerId",
                schema: "public",
                table: "static_networks",
                column: "VpnPeerId");

            migrationBuilder.CreateIndex(
                name: "IX_static_networks_VpnPeerId_Destination",
                schema: "public",
                table: "static_networks",
                columns: new[] { "VpnPeerId", "Destination" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_static_networks_vpn_peers_VpnPeerId",
                schema: "public",
                table: "static_networks",
                column: "VpnPeerId",
                principalSchema: "public",
                principalTable: "vpn_peers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateTable(
                name: "remote_networks",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VpnPeerId = table.Column<Guid>(type: "uuid", nullable: false),
                    NetworkCidr = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_remote_networks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_remote_networks_vpn_peers_VpnPeerId",
                        column: x => x.VpnPeerId,
                        principalSchema: "public",
                        principalTable: "vpn_peers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_remote_networks_VpnPeerId",
                schema: "public",
                table: "remote_networks",
                column: "VpnPeerId");

            migrationBuilder.CreateIndex(
                name: "IX_remote_networks_VpnPeerId_NetworkCidr",
                schema: "public",
                table: "remote_networks",
                columns: new[] { "VpnPeerId", "NetworkCidr" },
                unique: true);

            migrationBuilder.RenameColumn(
                name: "RouterAllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes",
                newName: "AllowedNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_user_allowed_routes_UserId_RouterAllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes",
                newName: "IX_user_allowed_routes_UserId_AllowedNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_user_allowed_routes_RouterAllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes",
                newName: "IX_user_allowed_routes_AllowedNetworkId");

            migrationBuilder.AddForeignKey(
                name: "FK_user_allowed_routes_allowed_networks_AllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes",
                column: "AllowedNetworkId",
                principalSchema: "public",
                principalTable: "allowed_networks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_user_allowed_routes_allowed_networks_AllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes");

            migrationBuilder.DropForeignKey(
                name: "FK_allowed_networks_vpn_peers_VpnPeerId",
                schema: "public",
                table: "allowed_networks");

            migrationBuilder.DropForeignKey(
                name: "FK_static_networks_vpn_peers_VpnPeerId",
                schema: "public",
                table: "static_networks");

            migrationBuilder.DropTable(
                name: "remote_networks",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_static_networks_VpnPeerId_Destination",
                schema: "public",
                table: "static_networks");

            migrationBuilder.DropIndex(
                name: "IX_static_networks_VpnPeerId",
                schema: "public",
                table: "static_networks");

            migrationBuilder.DropColumn(
                name: "VpnPeerId",
                schema: "public",
                table: "static_networks");

            migrationBuilder.RenameTable(
                name: "static_networks",
                schema: "public",
                newName: "router_static_routes");

            migrationBuilder.AddColumn<Guid>(
                name: "RouterId",
                schema: "public",
                table: "router_static_routes",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_router_static_routes_RouterId",
                schema: "public",
                table: "router_static_routes",
                column: "RouterId");

            migrationBuilder.CreateIndex(
                name: "IX_router_static_routes_RouterId_Destination",
                schema: "public",
                table: "router_static_routes",
                columns: new[] { "RouterId", "Destination" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_router_static_routes_routers_RouterId",
                schema: "public",
                table: "router_static_routes",
                column: "RouterId",
                principalSchema: "public",
                principalTable: "routers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropIndex(
                name: "IX_allowed_networks_VpnPeerId_NetworkCidr",
                schema: "public",
                table: "allowed_networks");

            migrationBuilder.DropIndex(
                name: "IX_allowed_networks_VpnPeerId",
                schema: "public",
                table: "allowed_networks");

            migrationBuilder.DropColumn(
                name: "VpnPeerId",
                schema: "public",
                table: "allowed_networks");

            migrationBuilder.RenameTable(
                name: "allowed_networks",
                schema: "public",
                newName: "router_allowed_networks");

            migrationBuilder.AddColumn<Guid>(
                name: "RouterId",
                schema: "public",
                table: "router_allowed_networks",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_router_allowed_networks_RouterId",
                schema: "public",
                table: "router_allowed_networks",
                column: "RouterId");

            migrationBuilder.CreateIndex(
                name: "IX_router_allowed_networks_RouterId_NetworkCidr",
                schema: "public",
                table: "router_allowed_networks",
                columns: new[] { "RouterId", "NetworkCidr" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_router_allowed_networks_routers_RouterId",
                schema: "public",
                table: "router_allowed_networks",
                column: "RouterId",
                principalSchema: "public",
                principalTable: "routers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.RenameColumn(
                name: "AllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes",
                newName: "RouterAllowedNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_user_allowed_routes_UserId_AllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes",
                newName: "IX_user_allowed_routes_UserId_RouterAllowedNetworkId");

            migrationBuilder.RenameIndex(
                name: "IX_user_allowed_routes_AllowedNetworkId",
                schema: "public",
                table: "user_allowed_routes",
                newName: "IX_user_allowed_routes_RouterAllowedNetworkId");

            migrationBuilder.AddForeignKey(
                name: "FK_user_allowed_routes_router_allowed_networks_RouterAllowedNe~",
                schema: "public",
                table: "user_allowed_routes",
                column: "RouterAllowedNetworkId",
                principalSchema: "public",
                principalTable: "router_allowed_networks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
