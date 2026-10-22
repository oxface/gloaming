using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales.Migrations;

/// <inheritdoc />
public partial class CustomerAddresses : Migration
{
    private static readonly string[] CustomerTenantColumns = ["OrganizationKey", "Id"];
    private static readonly string[] AddressCustomerColumns = ["OrganizationKey", "CustomerId"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_customer_reference_OrganizationKey_Id",
            schema: "sales",
            table: "customer_reference",
            columns: CustomerTenantColumns
        );

        migrationBuilder.CreateTable(
            name: "customer_address_reference",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationKey = table.Column<string>(type: "text", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                AddressLine = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_address_reference", x => x.Id);
                table.ForeignKey(
                    name: "FK_customer_address_reference_customer_tenant",
                    columns: x => new { x.OrganizationKey, x.CustomerId },
                    principalSchema: "sales",
                    principalTable: "customer_reference",
                    principalColumns: CustomerTenantColumns,
                    onDelete: ReferentialAction.Restrict
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_customer_address_reference_OrganizationKey_CustomerId",
            schema: "sales",
            table: "customer_address_reference",
            columns: AddressCustomerColumns
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_address_reference", schema: "sales");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_customer_reference_OrganizationKey_Id",
            schema: "sales",
            table: "customer_reference"
        );
    }
}
