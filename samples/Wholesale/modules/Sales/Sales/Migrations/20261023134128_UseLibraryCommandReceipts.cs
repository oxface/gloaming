using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class UseLibraryCommandReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "operation",
            schema: "sales",
            table: "command_receipts",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128
        );

        migrationBuilder.AlterColumn<string>(
            name: "organization_key",
            schema: "sales",
            table: "command_receipts",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            collation: "C",
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "operation",
            schema: "sales",
            table: "command_receipts",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128,
            oldCollation: "C"
        );

        migrationBuilder.AlterColumn<string>(
            name: "organization_key",
            schema: "sales",
            table: "command_receipts",
            type: "character varying(256)",
            maxLength: 256,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(256)",
            oldMaxLength: 256,
            oldCollation: "C"
        );
    }
}
