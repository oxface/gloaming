using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutboxDemo.Migrations;

/// <inheritdoc />
public partial class AddMessageTraceContext : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "trace_parent",
            schema: "exports",
            table: "outgoing_work",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "trace_state",
            schema: "exports",
            table: "outgoing_work",
            type: "text",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "trace_parent",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(name: "trace_state", schema: "exports", table: "outgoing_work");
    }
}
