using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddSalesCommandReceipts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "command_receipts",
            schema: "sales",
            columns: table => new
            {
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                operation = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false
                ),
                operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                format_version = table.Column<int>(type: "integer", nullable: false),
                input = table.Column<string>(type: "text", nullable: false),
                result = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_command_receipts",
                    x => new
                    {
                        x.organization_key,
                        x.operation,
                        x.operation_id,
                    }
                );
                table.CheckConstraint("ck_command_receipt_format", "format_version > 0");
            }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "command_receipts", schema: "sales");
    }
}
