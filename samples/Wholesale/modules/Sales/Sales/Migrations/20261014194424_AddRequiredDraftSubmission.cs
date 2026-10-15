using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddRequiredDraftSubmission : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_submitted",
            schema: "sales",
            table: "draft_orders",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.CreateTable(
            name: "customer_order_allowances",
            schema: "sales",
            columns: table => new
            {
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                currency = table.Column<string>(
                    type: "character varying(3)",
                    maxLength: 3,
                    nullable: false
                ),
                ceiling = table.Column<decimal>(type: "numeric", nullable: false),
                reserved = table.Column<decimal>(type: "numeric", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_customer_order_allowances",
                    x => new
                    {
                        x.organization_key,
                        x.customer_id,
                        x.currency,
                    }
                );
                table.CheckConstraint(
                    "ck_order_allowance_amounts",
                    "ceiling > 0 AND reserved >= 0 AND reserved <= ceiling"
                );
                table.CheckConstraint("ck_order_allowance_version", "version > 0");
                table.ForeignKey(
                    name: "FK_customer_order_allowances_customer_profiles_organization_ke~",
                    columns: x => new { x.organization_key, x.customer_id },
                    principalSchema: "sales",
                    principalTable: "customer_profiles",
                    principalColumns: ["organization_key", "id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_order_allowances", schema: "sales");

        migrationBuilder.DropColumn(name: "is_submitted", schema: "sales", table: "draft_orders");
    }
}
