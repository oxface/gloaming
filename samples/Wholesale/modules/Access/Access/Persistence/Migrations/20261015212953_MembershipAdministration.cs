using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Access.Persistence.Migrations;

/// <inheritdoc />
public partial class MembershipAdministration : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "can_administer_memberships",
            schema: "access",
            table: "memberships",
            type: "boolean",
            nullable: false,
            defaultValue: false
        );

        migrationBuilder.AddColumn<long>(
            name: "revision",
            schema: "access",
            table: "memberships",
            type: "bigint",
            nullable: false,
            defaultValue: 1L
        );

        migrationBuilder.CreateTable(
            name: "audit_entries",
            schema: "access",
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
                reason_code = table.Column<string>(type: "text", nullable: true),
                tenant_key = table.Column<string>(type: "text", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_entries", x => x.id);
            }
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_memberships_revision",
            schema: "access",
            table: "memberships",
            sql: "revision >= 1"
        );

        migrationBuilder.CreateIndex(
            name: "ix_audit_subject_timeline",
            schema: "access",
            table: "audit_entries",
            columns: ["tenant_key", "subject_type", "subject_key", "occurred_at", "id"]
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "audit_entries", schema: "access");

        migrationBuilder.DropCheckConstraint(
            name: "ck_memberships_revision",
            schema: "access",
            table: "memberships"
        );

        migrationBuilder.DropColumn(
            name: "can_administer_memberships",
            schema: "access",
            table: "memberships"
        );

        migrationBuilder.DropColumn(name: "revision", schema: "access", table: "memberships");
    }
}
