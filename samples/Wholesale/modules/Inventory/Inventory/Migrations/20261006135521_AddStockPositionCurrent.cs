using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddStockPositionCurrent : Migration
{
    private static readonly string[] HeaderColumns = ["organization_key", "id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "stock_position_current",
            schema: "inventory",
            columns: table => new
            {
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                stream_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                state = table.Column<JsonElement>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_stock_position_current",
                    x => new { x.organization_key, x.stream_id }
                );
                table.CheckConstraint("ck_stock_position_current_version", "version > 0");
                table.ForeignKey(
                    name: "FK_stock_position_current_event_streams_organization_key_strea~",
                    columns: x => new { x.organization_key, x.stream_id },
                    principalSchema: "inventory",
                    principalTable: "event_streams",
                    principalColumns: HeaderColumns,
                    onDelete: ReferentialAction.Restrict
                );
            }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "stock_position_current", schema: "inventory");
    }
}
