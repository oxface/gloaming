using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales.Migrations;

/// <inheritdoc />
public partial class CustomerVersions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "Version",
            schema: "sales",
            table: "customer_reference",
            type: "bigint",
            nullable: false,
            defaultValue: 1L
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Version", schema: "sales", table: "customer_reference");
    }
}
