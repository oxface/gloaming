using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.EventStorageDemo.Migrations;

/// <inheritdoc />
public partial class InitialJournal : Migration
{
    private static readonly string[] PositionColumns = ["stream_id", "stream_version"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "journal");

        migrationBuilder.CreateTable(
            name: "streams",
            schema: "journal",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                stream_type = table.Column<string>(
                    type: "character varying(100)",
                    maxLength: 100,
                    nullable: false
                ),
                version = table.Column<long>(type: "bigint", nullable: false),
                created_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                updated_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                description = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_streams", x => x.id);
                table.CheckConstraint("positive_stream_version", "version >= 1");
            }
        );

        migrationBuilder.CreateTable(
            name: "facts",
            schema: "journal",
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                stream_id = table.Column<Guid>(type: "uuid", nullable: false),
                stream_version = table.Column<long>(type: "bigint", nullable: false),
                event_name = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: false
                ),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_facts", x => x.event_id);
                table.CheckConstraint("positive_event_schema", "schema_version >= 1");
                table.CheckConstraint("positive_event_version", "stream_version >= 1");
                table.ForeignKey(
                    name: "FK_facts_streams_stream_id",
                    column: x => x.stream_id,
                    principalSchema: "journal",
                    principalTable: "streams",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_facts_stream_id_stream_version",
            schema: "journal",
            table: "facts",
            columns: PositionColumns,
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "facts", schema: "journal");

        migrationBuilder.DropTable(name: "streams", schema: "journal");
    }
}
