using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddDurableInboxAndMessageMetadata : Migration
{
    private static readonly string[] EligibleColumns =
    [
        "subscription_key",
        "available_at",
        "received_at",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "causation_id",
            schema: "inventory",
            table: "outbox_messages",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "correlation_id",
            schema: "inventory",
            table: "outbox_messages",
            type: "text",
            nullable: true
        );

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "inventory",
            columns: table => new
            {
                subscription_key = table.Column<string>(type: "text", nullable: false),
                producer_key = table.Column<string>(type: "text", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                message_name = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                tenant_key = table.Column<string>(type: "text", nullable: false),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                causation_id = table.Column<string>(type: "text", nullable: true),
                received_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                available_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                processed_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_inbox_messages",
                    x => new
                    {
                        x.subscription_key,
                        x.producer_key,
                        x.message_id,
                    }
                );
                table.CheckConstraint("ck_inbox_schema", "schema_version > 0");
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_subscription_key_available_at_received_at",
            schema: "inventory",
            table: "inbox_messages",
            columns: EligibleColumns,
            filter: "processed_at IS NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "inbox_messages", schema: "inventory");

        migrationBuilder.DropColumn(
            name: "causation_id",
            schema: "inventory",
            table: "outbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "correlation_id",
            schema: "inventory",
            table: "outbox_messages"
        );
    }
}
