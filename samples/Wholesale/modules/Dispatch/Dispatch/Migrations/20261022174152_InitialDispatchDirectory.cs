using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModulithFoundry.Samples.Wholesale.Dispatch.Migrations;

/// <inheritdoc />
public partial class InitialDispatchDirectory : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "dispatch");

        migrationBuilder.CreateTable(
            name: "customer_directory",
            schema: "dispatch",
            columns: table => new
            {
                Organization = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                Revision = table.Column<long>(type: "bigint", nullable: false),
                Listed = table.Column<bool>(type: "boolean", nullable: false),
                Code = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true
                ),
                DisplayName = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: true
                ),
                AddressLine = table.Column<string>(
                    type: "character varying(512)",
                    maxLength: 512,
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_customer_directory",
                    x => new { x.Organization, x.CustomerId }
                );
            }
        );

        migrationBuilder.CreateTable(
            name: "directory_populations",
            schema: "dispatch",
            columns: table => new
            {
                Organization = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                PopulationId = table.Column<Guid>(type: "uuid", nullable: false),
                After = table.Column<Guid>(type: "uuid", nullable: true),
                Ready = table.Column<bool>(type: "boolean", nullable: false),
                Copied = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_directory_populations", x => x.Organization);
            }
        );

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "dispatch",
            columns: table => new
            {
                subscription_key = table.Column<string>(type: "text", nullable: false),
                producer_key = table.Column<string>(type: "text", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                receipt_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false,
                    defaultValueSql: "gen_random_uuid()"
                ),
                message_name = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                tenant_key = table.Column<string>(type: "text", nullable: true),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                causation_id = table.Column<string>(type: "text", nullable: true),
                trace_parent = table.Column<string>(type: "text", nullable: true),
                trace_state = table.Column<string>(type: "text", nullable: true),
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
                revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                failure_count = table.Column<long>(
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L
                ),
                failures_since_redrive = table.Column<long>(
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L
                ),
                last_failed_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                last_failure_code = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: true
                ),
                quarantined_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                redrive_count = table.Column<long>(
                    type: "bigint",
                    nullable: false,
                    defaultValue: 0L
                ),
                last_redriven_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_inbox_messages",
                    x => new
                    {
                        x.subscription_key,
                        x.producer_key,
                        x.message_id,
                    }
                );
                table.CheckConstraint(
                    "ck_inbox_failure_metadata",
                    "(failure_count = 0 AND last_failed_at IS NULL AND last_failure_code IS NULL AND quarantined_at IS NULL) OR (failure_count > 0 AND last_failed_at IS NOT NULL AND last_failure_code IS NOT NULL AND length(btrim(last_failure_code)) > 0)"
                );
                table.CheckConstraint(
                    "ck_inbox_receipt",
                    "receipt_id <> '00000000-0000-0000-0000-000000000000'::uuid"
                );
                table.CheckConstraint(
                    "ck_inbox_recovery_counts",
                    "revision >= 0 AND failure_count >= 0 AND failures_since_redrive >= 0 AND failures_since_redrive <= failure_count AND redrive_count >= 0"
                );
                table.CheckConstraint(
                    "ck_inbox_recovery_state",
                    "processed_at IS NULL OR quarantined_at IS NULL"
                );
                table.CheckConstraint(
                    "ck_inbox_redrive_metadata",
                    "(redrive_count = 0 AND last_redriven_at IS NULL) OR (redrive_count > 0 AND last_redriven_at IS NOT NULL)"
                );
                table.CheckConstraint("ck_inbox_schema", "schema_version > 0");
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_subscription_key_available_at_received_at",
            schema: "dispatch",
            table: "inbox_messages",
            columns: ["subscription_key", "available_at", "received_at"],
            filter: "processed_at IS NULL AND quarantined_at IS NULL"
        );

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_subscription_key_producer_key_quarantined_at~",
            schema: "dispatch",
            table: "inbox_messages",
            columns: ["subscription_key", "producer_key", "quarantined_at", "message_id"],
            filter: "processed_at IS NULL AND quarantined_at IS NOT NULL"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_directory", schema: "dispatch");

        migrationBuilder.DropTable(name: "directory_populations", schema: "dispatch");

        migrationBuilder.DropTable(name: "inbox_messages", schema: "dispatch");
    }
}
