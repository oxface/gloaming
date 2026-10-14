using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddCustomerProfileFeed : Migration
{
    private static readonly string[] ChangeIdentityColumns = ["organization_key", "change_id"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(name: "customer_profile_feed_positions", schema: "sales");

        migrationBuilder.CreateTable(
            name: "customer_profile_feed_state",
            schema: "sales",
            columns: table => new
            {
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                generation = table.Column<Guid>(type: "uuid", nullable: false),
                head = table.Column<long>(type: "bigint", nullable: false),
                floor = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_profile_feed_state", x => x.organization_key);
                table.CheckConstraint("feed_bounds", "floor >= 0 AND head >= floor");
                table.CheckConstraint(
                    "feed_generation",
                    "generation <> '00000000-0000-0000-0000-000000000000'::uuid"
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "customer_profile_changes",
            schema: "sales",
            columns: table => new
            {
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                position = table.Column<long>(type: "bigint", nullable: false),
                change_id = table.Column<Guid>(type: "uuid", nullable: false),
                profile_version = table.Column<long>(type: "bigint", nullable: false),
                payload_v1 = table.Column<JsonElement>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_customer_profile_changes",
                    x => new { x.organization_key, x.position }
                );
                table.CheckConstraint(
                    "feed_change_identity",
                    "change_id <> '00000000-0000-0000-0000-000000000000'::uuid"
                );
                table.CheckConstraint("feed_position", "position > 0");
                table.CheckConstraint("feed_profile_version", "profile_version > 0");
                table.ForeignKey(
                    name: "FK_customer_profile_changes_customer_profile_feed_state_organi~",
                    column: x => x.organization_key,
                    principalSchema: "sales",
                    principalTable: "customer_profile_feed_state",
                    principalColumn: "organization_key",
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_customer_profile_changes_organization_key_change_id",
            schema: "sales",
            table: "customer_profile_changes",
            columns: ChangeIdentityColumns,
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_customer_profile_feed_state_generation",
            schema: "sales",
            table: "customer_profile_feed_state",
            column: "generation",
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_profile_changes", schema: "sales");

        migrationBuilder.DropTable(name: "customer_profile_feed_state", schema: "sales");

        migrationBuilder.DropSequence(name: "customer_profile_feed_positions", schema: "sales");
    }
}
