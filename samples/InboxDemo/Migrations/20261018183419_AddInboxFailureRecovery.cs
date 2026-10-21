using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InboxDemo.Migrations;

/// <inheritdoc />
public partial class AddInboxFailureRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_incoming_work_subscription_key_available_at_received_at",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.AddColumn<long>(
            name: "failure_count",
            schema: "rendering",
            table: "incoming_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<long>(
            name: "failures_since_redrive",
            schema: "rendering",
            table: "incoming_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_failed_at",
            schema: "rendering",
            table: "incoming_work",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "last_failure_code",
            schema: "rendering",
            table: "incoming_work",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_redriven_at",
            schema: "rendering",
            table: "incoming_work",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "quarantined_at",
            schema: "rendering",
            table: "incoming_work",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<long>(
            name: "redrive_count",
            schema: "rendering",
            table: "incoming_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<long>(
            name: "revision",
            schema: "rendering",
            table: "incoming_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.CreateIndex(
            name: "IX_incoming_work_subscription_key_available_at_received_at",
            schema: "rendering",
            table: "incoming_work",
            columns: ["subscription_key", "available_at", "received_at"],
            filter: "processed_at IS NULL AND quarantined_at IS NULL"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_failure_metadata",
            schema: "rendering",
            table: "incoming_work",
            sql: "(failure_count = 0 AND last_failed_at IS NULL AND last_failure_code IS NULL AND quarantined_at IS NULL) OR (failure_count > 0 AND last_failed_at IS NOT NULL AND last_failure_code IS NOT NULL AND length(btrim(last_failure_code)) > 0)"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_recovery_counts",
            schema: "rendering",
            table: "incoming_work",
            sql: "revision >= 0 AND failure_count >= 0 AND failures_since_redrive >= 0 AND failures_since_redrive <= failure_count AND redrive_count >= 0"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_recovery_state",
            schema: "rendering",
            table: "incoming_work",
            sql: "processed_at IS NULL OR quarantined_at IS NULL"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_redrive_metadata",
            schema: "rendering",
            table: "incoming_work",
            sql: "(redrive_count = 0 AND last_redriven_at IS NULL) OR (redrive_count > 0 AND last_redriven_at IS NOT NULL)"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_incoming_work_subscription_key_available_at_received_at",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_failure_metadata",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_recovery_counts",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_recovery_state",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_redrive_metadata",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "failure_count",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "failures_since_redrive",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "last_failed_at",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "last_failure_code",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "last_redriven_at",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "quarantined_at",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(
            name: "redrive_count",
            schema: "rendering",
            table: "incoming_work"
        );

        migrationBuilder.DropColumn(name: "revision", schema: "rendering", table: "incoming_work");

        migrationBuilder.CreateIndex(
            name: "IX_incoming_work_subscription_key_available_at_received_at",
            schema: "rendering",
            table: "incoming_work",
            columns: ["subscription_key", "available_at", "received_at"],
            filter: "processed_at IS NULL"
        );
    }
}
