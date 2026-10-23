using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Sales.Migrations;

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
            name: "IX_inbox_messages_subscription_key_producer_key_quarantined_at~",
            schema: "sales",
            table: "inbox_messages",
            columns: LookupColumns,
            filter: "processed_at IS NULL AND quarantined_at IS NOT NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_inbox_messages_subscription_key_producer_key_quarantined_at~",
            schema: "sales",
            table: "inbox_messages"
        );
    }
}
