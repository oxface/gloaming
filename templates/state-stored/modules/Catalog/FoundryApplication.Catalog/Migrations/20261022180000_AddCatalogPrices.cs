using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ConsumerRoot.Catalog.Migrations;

[DbContext(typeof(CatalogDbContext))]
[Migration("20261022180000_AddCatalogPrices")]
public sealed class AddCatalogPrices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "price",
            schema: "catalog",
            table: "items",
            type: "numeric(18,2)",
            nullable: false,
            defaultValue: 0m
        );
        migrationBuilder.AddColumn<long>(
            name: "version",
            schema: "catalog",
            table: "items",
            type: "bigint",
            nullable: false,
            defaultValue: 1L
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "price", schema: "catalog", table: "items");
        migrationBuilder.DropColumn(name: "version", schema: "catalog", table: "items");
    }
}
