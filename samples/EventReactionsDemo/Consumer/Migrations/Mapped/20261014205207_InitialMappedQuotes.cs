using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventReactionsDemo.Migrations.Mapped;

/// <inheritdoc />
public partial class InitialMappedQuotes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "mapped_quotes");

        migrationBuilder.CreateTable(
            name: "allowances",
            schema: "mapped_quotes",
            columns: table => new
            {
                Owner = table.Column<string>(type: "text", nullable: false),
                Ceiling = table.Column<decimal>(type: "numeric", nullable: false),
                Reserved = table.Column<decimal>(type: "numeric", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_allowances", x => x.Owner);
            }
        );

        migrationBuilder.CreateTable(
            name: "quotes",
            schema: "mapped_quotes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Owner = table.Column<string>(type: "text", nullable: false),
                Amount = table.Column<decimal>(type: "numeric", nullable: false),
                Accepted = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_quotes", x => new { x.Owner, x.Id });
            }
        );

        migrationBuilder.CreateTable(
            name: "allocations",
            schema: "mapped_quotes",
            columns: table => new
            {
                Owner = table.Column<string>(type: "text", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                Amount = table.Column<decimal>(type: "numeric", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_allocations", x => new { x.Owner, x.OrderId });
                table.ForeignKey(
                    name: "FK_allocations_quotes_Owner_OrderId",
                    columns: x => new { x.Owner, x.OrderId },
                    principalSchema: "mapped_quotes",
                    principalTable: "quotes",
                    principalColumns: ["Owner", "Id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "receipts",
            schema: "mapped_quotes",
            columns: table => new
            {
                Owner = table.Column<string>(type: "text", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_receipts", x => new { x.Owner, x.OrderId });
                table.ForeignKey(
                    name: "FK_receipts_quotes_Owner_OrderId",
                    columns: x => new { x.Owner, x.OrderId },
                    principalSchema: "mapped_quotes",
                    principalTable: "quotes",
                    principalColumns: ["Owner", "Id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "allocations", schema: "mapped_quotes");

        migrationBuilder.DropTable(name: "allowances", schema: "mapped_quotes");

        migrationBuilder.DropTable(name: "receipts", schema: "mapped_quotes");

        migrationBuilder.DropTable(name: "quotes", schema: "mapped_quotes");
    }
}
