using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddStockReservations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "stock_reservations",
            schema: "inventory",
            columns: table => new
            {
                OrganizationKey = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StockPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                ExpectedVersion = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                Decision = table.Column<int>(type: "integer", nullable: false),
                Released = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_reservations", x => new { x.OrganizationKey, x.Id });
                table.CheckConstraint(
                    "valid_reservation",
                    "\"Quantity\" > 0 AND \"ExpectedVersion\" >= 1 AND \"Version\" >= 1 AND \"Decision\" BETWEEN 1 AND 4 AND (NOT \"Released\" OR \"Decision\" = 1)"
                );
            }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "stock_reservations", schema: "inventory");
    }
}
