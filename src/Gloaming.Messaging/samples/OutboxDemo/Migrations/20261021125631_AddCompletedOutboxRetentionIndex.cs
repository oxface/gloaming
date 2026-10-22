using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OutboxDemo.Migrations;

/// <inheritdoc />
public partial class AddCompletedOutboxRetentionIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_exports_outbox_completed_retention",
            schema: "exports",
            table: "outgoing_work",
            columns: ["destination", "dispatched_at", "message_id"],
            filter: "dispatched_at IS NOT NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_exports_outbox_completed_retention",
            schema: "exports",
            table: "outgoing_work"
        );
    }
}
