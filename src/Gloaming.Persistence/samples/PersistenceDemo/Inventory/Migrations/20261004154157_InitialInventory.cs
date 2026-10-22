using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Inventory.Migrations;

/// <inheritdoc />
public partial class InitialInventory : Migration
{
    private static readonly string[] OrganizationSkuColumns = ["OrganizationKey", "Sku"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "inventory");

        migrationBuilder.CreateTable(
            name: "reference_category",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "text", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reference_category", x => x.Id);
            }
        );

        migrationBuilder.CreateTable(
            name: "stock_reference",
            schema: "inventory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OrganizationKey = table.Column<string>(type: "text", nullable: false),
                Sku = table.Column<string>(type: "text", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_reference", x => x.Id);
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_stock_reference_OrganizationKey_Sku",
            schema: "inventory",
            table: "stock_reference",
            columns: OrganizationSkuColumns,
            unique: true
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "reference_category", schema: "inventory");

        migrationBuilder.DropTable(name: "stock_reference", schema: "inventory");
    }
}
