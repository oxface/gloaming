using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxDemo.Migrations;

/// <inheritdoc />
public partial class AddMessageTraceContext : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "trace_parent",
            schema: "rendering",
            table: "incoming_work",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "trace_state",
            schema: "rendering",
            table: "incoming_work",
            type: "text",
            nullable: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "trace_parent",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "trace_state",
            schema: "rendering",
            table: "incoming_work"
        );
    }
}
