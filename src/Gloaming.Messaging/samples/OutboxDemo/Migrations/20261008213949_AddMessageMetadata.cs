using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutboxDemo.Migrations;

/// <inheritdoc />
public partial class AddMessageMetadata : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "causation_id",
            schema: "exports",
            table: "outgoing_work",
            type: "text",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "correlation_id",
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
            name: "causation_id",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "correlation_id",
            schema: "exports",
            table: "outgoing_work"
        );
    }
}
