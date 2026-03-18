using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Automais.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VpnNetworkListenPort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ListenPort",
                schema: "public",
                table: "vpn_networks",
                type: "integer",
                nullable: false,
                defaultValue: 51820);

            migrationBuilder.Sql(@"
UPDATE vpn_networks AS vn
SET ""ListenPort"" = sub.mx
FROM (
  SELECT ""VpnNetworkId"" AS nid, MAX(""ListenPort"") AS mx
  FROM router_wireguard_peers
  WHERE ""ListenPort"" IS NOT NULL
  GROUP BY ""VpnNetworkId""
) AS sub
WHERE vn.""Id"" = sub.nid;
");

            migrationBuilder.Sql(@"
DO $$
DECLARE
  ep text;
  rec record;
  used_ports int[];
  p int;
BEGIN
  FOR ep IN SELECT DISTINCT LOWER(TRIM(COALESCE(""ServerEndpoint"", ''))) FROM vpn_networks
  LOOP
    used_ports := ARRAY[]::int[];
    FOR rec IN
      SELECT ""Id"", ""ListenPort"", ""CreatedAt"" FROM vpn_networks
      WHERE LOWER(TRIM(COALESCE(""ServerEndpoint"", ''))) = ep
      ORDER BY ""CreatedAt""
    LOOP
      p := rec.""ListenPort"";
      IF p IS NULL OR p < 1 OR p > 65535 THEN p := 51820; END IF;
      WHILE p = ANY(used_ports) LOOP
        p := p + 1;
      END LOOP;
      used_ports := array_append(used_ports, p);
      UPDATE vpn_networks SET ""ListenPort"" = p WHERE ""Id"" = rec.""Id"";
    END LOOP;
  END LOOP;
END $$;
");

            migrationBuilder.DropColumn(
                name: "ListenPort",
                schema: "public",
                table: "router_wireguard_peers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ListenPort",
                schema: "public",
                table: "router_wireguard_peers",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE router_wireguard_peers AS p
SET ""ListenPort"" = n.""ListenPort""
FROM vpn_networks AS n
WHERE p.""VpnNetworkId"" = n.""Id"";
");

            migrationBuilder.DropColumn(
                name: "ListenPort",
                schema: "public",
                table: "vpn_networks");
        }
    }
}
