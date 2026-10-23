using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Access.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialAccess : Migration
{
    private static readonly string[] CurrentMembershipColumns = ["organization_id", "user_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "access");

        migrationBuilder.CreateTable(
            name: "organizations",
            schema: "access",
            columns: table => new
            {
                id = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                slug = table.Column<string>(
                    type: "character varying(63)",
                    maxLength: 63,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_organizations", x => x.id);
                table.CheckConstraint(
                    "ck_organizations_slug",
                    "slug ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$'"
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "users",
            schema: "access",
            columns: table => new
            {
                id = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.id);
            }
        );

        migrationBuilder.CreateTable(
            name: "external_identities",
            schema: "access",
            columns: table => new
            {
                issuer = table.Column<string>(
                    type: "character varying(512)",
                    maxLength: 512,
                    nullable: false
                ),
                subject = table.Column<string>(
                    type: "character varying(255)",
                    maxLength: 255,
                    nullable: false
                ),
                user_id = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_external_identities", x => new { x.issuer, x.subject });
                table.ForeignKey(
                    name: "FK_external_identities_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "access",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "memberships",
            schema: "access",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                user_id = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                status = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_memberships", x => x.id);
                table.CheckConstraint("ck_memberships_status", "status IN (1, 2, 3)");
                table.ForeignKey(
                    name: "FK_memberships_organizations_organization_id",
                    column: x => x.organization_id,
                    principalSchema: "access",
                    principalTable: "organizations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict
                );
                table.ForeignKey(
                    name: "FK_memberships_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "access",
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_external_identities_user_id",
            schema: "access",
            table: "external_identities",
            column: "user_id"
        );

        migrationBuilder.CreateIndex(
            name: "IX_memberships_organization_id_user_id",
            schema: "access",
            table: "memberships",
            columns: CurrentMembershipColumns,
            unique: true,
            filter: "status IN (1, 2)"
        );

        migrationBuilder.CreateIndex(
            name: "IX_memberships_user_id",
            schema: "access",
            table: "memberships",
            column: "user_id"
        );

        migrationBuilder.CreateIndex(
            name: "IX_organizations_slug",
            schema: "access",
            table: "organizations",
            column: "slug",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "external_identities", schema: "access");

        migrationBuilder.DropTable(name: "memberships", schema: "access");

        migrationBuilder.DropTable(name: "organizations", schema: "access");

        migrationBuilder.DropTable(name: "users", schema: "access");
    }
}
