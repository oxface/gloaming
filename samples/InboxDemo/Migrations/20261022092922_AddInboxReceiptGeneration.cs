using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxDemo.Migrations;

/// <inheritdoc />
public partial class AddInboxReceiptGeneration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "receipt_id",
            schema: "rendering",
            table: "incoming_work",
            type: "uuid",
            nullable: false,
            defaultValueSql: "gen_random_uuid()"
        );

        migrationBuilder.CreateIndex(
            name: "IX_incoming_work_subscription_key_producer_key_processed_at_me~",
            schema: "rendering",
            table: "incoming_work",
            columns: ["subscription_key", "producer_key", "processed_at", "message_id"],
            filter: "processed_at IS NOT NULL AND quarantined_at IS NULL"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_receipt",
            schema: "rendering",
            table: "incoming_work",
            sql: "receipt_id <> '00000000-0000-0000-0000-000000000000'::uuid"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_incoming_work_subscription_key_producer_key_processed_at_me~",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_receipt",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "receipt_id",
            schema: "rendering",
            table: "incoming_work"
        );
    }
}
