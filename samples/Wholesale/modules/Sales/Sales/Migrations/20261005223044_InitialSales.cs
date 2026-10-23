using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class InitialSales : Migration
{
    private static readonly string[] CustomerKeyColumns = ["organization_key", "id"];
    private static readonly string[] AddressOwnerColumns = ["organization_key", "customer_id"];
    private static readonly string[] CustomerCodeColumns = ["organization_key", "code"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "sales");

        migrationBuilder.CreateTable(
            name: "customer_profiles",
            schema: "sales",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                code = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false
                ),
                display_name = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_profiles", x => x.id);
                table.UniqueConstraint(
                    "AK_customer_profiles_organization_key_id",
                    x => new { x.organization_key, x.id }
                );
                table.CheckConstraint("positive_version", "version >= 1");
            }
        );

        migrationBuilder.CreateTable(
            name: "customer_addresses",
            schema: "sales",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_key = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                address_line = table.Column<string>(
                    type: "character varying(512)",
                    maxLength: 512,
                    nullable: false
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_addresses", x => x.id);
                table.ForeignKey(
                    name: "FK_customer_addresses_customer_profiles_organization_key_custo~",
                    columns: x => new { x.organization_key, x.customer_id },
                    principalSchema: "sales",
                    principalTable: "customer_profiles",
                    principalColumns: CustomerKeyColumns,
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_customer_addresses_organization_key_customer_id",
            schema: "sales",
            table: "customer_addresses",
            columns: AddressOwnerColumns,
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_customer_profiles_organization_key_code",
            schema: "sales",
            table: "customer_profiles",
            columns: CustomerCodeColumns,
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_addresses", schema: "sales");

        migrationBuilder.DropTable(name: "customer_profiles", schema: "sales");
    }
}
