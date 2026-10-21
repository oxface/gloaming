using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxDemo.Migrations;

/// <inheritdoc />
public partial class AddInboxDeadLetterLookup : Migration
{
    private static readonly string[] LookupColumns =
    [
        "subscription_key",
        "producer_key",
        "quarantined_at",
        "message_id",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_incoming_work_subscription_key_producer_key_quarantined_at_~",
            schema: "rendering",
            table: "incoming_work",
            columns: LookupColumns,
            filter: "processed_at IS NULL AND quarantined_at IS NOT NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_incoming_work_subscription_key_producer_key_quarantined_at_~",
            schema: "rendering",
            table: "incoming_work"
        );
    }
}
