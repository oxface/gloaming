using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales.Migrations;

/// <inheritdoc />
public partial class InitialSales : Migration
{
    private static readonly string[] OrganizationCodeColumns = ["OrganizationKey", "Code"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "sales");

        migrationBuilder.CreateTable(
            name: "customer_reference",
            schema: "sales",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationKey = table.Column<string>(type: "text", nullable: false),
                Code = table.Column<string>(type: "text", nullable: false),
                DisplayName = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_reference", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_customer_reference_OrganizationKey_Code",
            schema: "sales",
            table: "customer_reference",
            columns: OrganizationCodeColumns,
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_reference", schema: "sales");
    }
}
