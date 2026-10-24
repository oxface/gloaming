using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddTransactionalAudit : Migration
{
    private static readonly string[] AuditIndexColumns =
    [
        "tenant_key",
        "subject_type",
        "subject_key",
        "occurred_at",
        "id",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_entries",
            schema: "inventory",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                actor_kind = table.Column<int>(type: "integer", nullable: false),
                actor_key = table.Column<string>(type: "text", nullable: true),
                initiator_kind = table.Column<int>(type: "integer", nullable: true),
                initiator_key = table.Column<string>(type: "text", nullable: true),
                source = table.Column<string>(type: "text", nullable: false),
                action = table.Column<string>(type: "text", nullable: false),
                subject_type = table.Column<string>(type: "text", nullable: false),
                subject_key = table.Column<string>(type: "text", nullable: true),
                outcome = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                details = table.Column<JsonElement>(type: "jsonb", nullable: false),
                tenant_key = table.Column<string>(type: "text", nullable: false),
                reason_code = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_entries", x => x.id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "ix_audit_subject_timeline",
            schema: "inventory",
            table: "audit_entries",
            columns: AuditIndexColumns
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "audit_entries", schema: "inventory");
    }
}
