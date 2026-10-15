using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventReactionsDemo.Migrations.Direct;

/// <inheritdoc />
public partial class InitialDirectOrders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "direct_orders");

        migrationBuilder.CreateTable(
            name: "allowances",
            schema: "direct_orders",
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
            name: "orders",
            schema: "direct_orders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Owner = table.Column<string>(type: "text", nullable: false),
                Amount = table.Column<decimal>(type: "numeric", nullable: false),
                Submitted = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_orders", x => new { x.Owner, x.Id });
            }
        );

        migrationBuilder.CreateTable(
            name: "allocations",
            schema: "direct_orders",
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
                    name: "FK_allocations_orders_Owner_OrderId",
                    columns: x => new { x.Owner, x.OrderId },
                    principalSchema: "direct_orders",
                    principalTable: "orders",
                    principalColumns: ["Owner", "Id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "receipts",
            schema: "direct_orders",
            columns: table => new
            {
                Owner = table.Column<string>(type: "text", nullable: false),
                OrderId = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_receipts", x => new { x.Owner, x.OrderId });
                table.ForeignKey(
                    name: "FK_receipts_orders_Owner_OrderId",
                    columns: x => new { x.Owner, x.OrderId },
                    principalSchema: "direct_orders",
                    principalTable: "orders",
                    principalColumns: ["Owner", "Id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "allocations", schema: "direct_orders");

        migrationBuilder.DropTable(name: "allowances", schema: "direct_orders");

        migrationBuilder.DropTable(name: "receipts", schema: "direct_orders");

        migrationBuilder.DropTable(name: "orders", schema: "direct_orders");
    }
}
