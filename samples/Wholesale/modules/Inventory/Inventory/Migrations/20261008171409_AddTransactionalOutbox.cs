using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddTransactionalOutbox : Migration
{
    private static readonly string[] EligibleColumns =
    [
        "available_at",
        "lease_until",
        "message_id",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "inventory",
            columns: table => new
            {
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination = table.Column<string>(type: "text", nullable: false),
                message_name = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                owner_key = table.Column<string>(type: "text", nullable: false),
                queued_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                available_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                dispatched_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                attempts = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.message_id);
                table.CheckConstraint("ck_outbox_attempts", "attempts >= 0");
                table.CheckConstraint(
                    "ck_outbox_lease",
                    "(lease_token IS NULL) = (lease_until IS NULL)"
                );
                table.CheckConstraint("ck_outbox_schema", "schema_version > 0");
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_available_at_lease_until_message_id",
            schema: "inventory",
            table: "outbox_messages",
            columns: EligibleColumns,
            filter: "dispatched_at IS NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "outbox_messages", schema: "inventory");
    }
}
