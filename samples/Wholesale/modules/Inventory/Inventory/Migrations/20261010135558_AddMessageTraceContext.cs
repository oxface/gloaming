using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddMessageTraceContext : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "trace_parent",
            schema: "inventory",
            table: "outbox_messages",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "trace_state",
            schema: "inventory",
            table: "outbox_messages",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "trace_parent",
            schema: "inventory",
            table: "inbox_messages",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "trace_state",
            schema: "inventory",
            table: "inbox_messages",
            type: "text",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "trace_parent",
            schema: "inventory",
            table: "outbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "trace_state",
            schema: "inventory",
            table: "outbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "trace_parent",
            schema: "inventory",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "trace_state",
            schema: "inventory",
            table: "inbox_messages"
        );
    }
}
