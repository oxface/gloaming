using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Inventory.Migrations;

/// <inheritdoc />
public partial class AddEventStreamConcurrencyStamp : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "concurrency_stamp",
            schema: "inventory",
            table: "event_streams",
            type: "uuid",
            nullable: false,
            defaultValueSql: "gen_random_uuid()"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "concurrency_stamp",
            schema: "inventory",
            table: "event_streams"
        );
    }
}
