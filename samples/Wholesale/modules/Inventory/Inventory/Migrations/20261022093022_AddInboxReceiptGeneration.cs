using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddInboxReceiptGeneration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "receipt_id",
            schema: "inventory",
            table: "inbox_messages",
            type: "uuid",
            nullable: false,
            defaultValueSql: "gen_random_uuid()"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_receipt",
            schema: "inventory",
            table: "inbox_messages",
            sql: "receipt_id <> '00000000-0000-0000-0000-000000000000'::uuid"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_receipt",
            schema: "inventory",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "receipt_id",
            schema: "inventory",
            table: "inbox_messages"
        );
    }
}
