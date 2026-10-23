using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddCompensatingReservationOrders : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "workflow_instances",
            schema: "sales",
            columns: table => new
            {
                WorkflowType = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantKey = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                DefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                ContinuationName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                PendingCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                InitialInput = table.Column<JsonElement>(type: "jsonb", nullable: false),
                State = table.Column<JsonElement>(type: "jsonb", nullable: false),
                Operations = table.Column<JsonElement>(type: "jsonb", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_workflow_instances", x => new { x.WorkflowType, x.Id });
            }
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "workflow_instances", schema: "sales");
    }
}
