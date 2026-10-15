using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddDraftOrderChildren : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "draft_orders",
            schema: "sales",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                code = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false
                ),
                version = table.Column<long>(type: "bigint", nullable: false),
                currency = table.Column<string>(
                    type: "character varying(3)",
                    maxLength: 3,
                    nullable: false
                ),
                total_limit = table.Column<decimal>(type: "numeric", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_draft_orders", x => new { x.organization_key, x.id });
                table.CheckConstraint("ck_draft_order_limit", "total_limit > 0");
                table.CheckConstraint("ck_draft_order_version", "version > 0");
                table.ForeignKey(
                    name: "fk_draft_orders_customer",
                    columns: x => new { x.organization_key, x.customer_id },
                    principalSchema: "sales",
                    principalTable: "customer_profiles",
                    principalColumns: ["organization_key", "id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "draft_order_lines",
            schema: "sales",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                sku = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false
                ),
                quantity = table.Column<decimal>(type: "numeric", nullable: false),
                unit_price = table.Column<decimal>(type: "numeric", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_draft_order_lines",
                    x => new
                    {
                        x.organization_key,
                        x.order_id,
                        x.id,
                    }
                );
                table.CheckConstraint("ck_draft_line_price", "unit_price >= 0");
                table.CheckConstraint("ck_draft_line_quantity", "quantity > 0");
                table.ForeignKey(
                    name: "fk_draft_order_lines_order",
                    columns: x => new { x.organization_key, x.order_id },
                    principalSchema: "sales",
                    principalTable: "draft_orders",
                    principalColumns: ["organization_key", "id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_draft_orders_organization_key_customer_id",
            schema: "sales",
            table: "draft_orders",
            columns: ["organization_key", "customer_id"]
        );

        migrationBuilder.CreateIndex(
            name: "ux_draft_orders_code",
            schema: "sales",
            table: "draft_orders",
            columns: ["organization_key", "code"],
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "draft_order_lines", schema: "sales");

        migrationBuilder.DropTable(name: "draft_orders", schema: "sales");
    }
}
