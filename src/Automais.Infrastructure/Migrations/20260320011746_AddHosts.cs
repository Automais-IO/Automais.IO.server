using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hosts",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HostKind = table.Column<string>(type: "text", nullable: false),
                    VpnNetworkId = table.Column<Guid>(type: "uuid", nullable: true),
                    VpnIp = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SshPort = table.Column<int>(type: "integer", nullable: false),
                    SshUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SshPassword = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_hosts_tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "public",
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_hosts_vpn_networks_VpnNetworkId",
                        column: x => x.VpnNetworkId,
                        principalSchema: "public",
                        principalTable: "vpn_networks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hosts_TenantId",
                schema: "public",
                table: "hosts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_hosts_VpnNetworkId",
                schema: "public",
                table: "hosts",
                column: "VpnNetworkId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hosts",
                schema: "public");
        }
    }
}
