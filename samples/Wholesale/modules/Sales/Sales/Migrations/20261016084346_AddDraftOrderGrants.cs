using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddDraftOrderGrants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "draft_order_grants",
            schema: "sales",
            columns: table => new
            {
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                order_id = table.Column<Guid>(type: "uuid", nullable: false),
                subject_kind = table.Column<int>(type: "integer", nullable: false),
                subject_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                permission = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_draft_order_grants",
                    x => new
                    {
                        x.organization_key,
                        x.order_id,
                        x.subject_kind,
                        x.subject_key,
                        x.permission,
                    }
                );
                table.CheckConstraint("ck_draft_grant_key", "subject_key ~ '[^[:space:]]'");
                table.CheckConstraint("ck_draft_grant_permission", "permission IN (1, 2)");
                table.CheckConstraint("ck_draft_grant_subject", "subject_kind IN (2, 3)");
                table.ForeignKey(
                    name: "FK_draft_order_grants_draft_orders_organization_key_order_id",
                    columns: x => new { x.organization_key, x.order_id },
                    principalSchema: "sales",
                    principalTable: "draft_orders",
                    principalColumns: ["organization_key", "id"],
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_draft_order_grants_organization_key_subject_kind_subject_ke~",
            schema: "sales",
            table: "draft_order_grants",
            columns: ["organization_key", "subject_kind", "subject_key", "order_id"]
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "draft_order_grants", schema: "sales");
    }
}
