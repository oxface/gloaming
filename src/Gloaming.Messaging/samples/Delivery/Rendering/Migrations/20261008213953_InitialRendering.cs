using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxDemo.Migrations;

/// <inheritdoc />
public partial class InitialRendering : Migration
{
    private static readonly string[] EligibleColumns =
    [
        "subscription_key",
        "available_at",
        "received_at",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "rendering");

        migrationBuilder.CreateTable(
            name: "incoming_work",
            schema: "rendering",
            columns: table => new
            {
                subscription_key = table.Column<string>(type: "text", nullable: false),
                producer_key = table.Column<string>(type: "text", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                message_name = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                tenant_key = table.Column<string>(type: "text", nullable: true),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                causation_id = table.Column<string>(type: "text", nullable: true),
                received_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                available_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                processed_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_incoming_work",
                    x => new
                    {
                        x.subscription_key,
                        x.producer_key,
                        x.message_id,
                    }
                );
                table.CheckConstraint("ck_inbox_schema", "schema_version > 0");
            }
        );

        migrationBuilder.CreateTable(
            name: "jobs",
            schema: "rendering",
            columns: table => new
            {
                ExportRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                Pages = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_jobs", x => x.ExportRequestId);
                table.CheckConstraint("ck_job_pages", "\"Pages\" > 0");
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_incoming_work_subscription_key_available_at_received_at",
            schema: "rendering",
            table: "incoming_work",
            columns: EligibleColumns,
            filter: "processed_at IS NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "incoming_work", schema: "rendering");

        migrationBuilder.DropTable(name: "jobs", schema: "rendering");
    }
}
