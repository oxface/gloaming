using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddInboxFailureRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_inbox_messages_subscription_key_available_at_received_at",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.AddColumn<long>(
            name: "failure_count",
            schema: "sales",
            table: "inbox_messages",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<long>(
            name: "failures_since_redrive",
            schema: "sales",
            table: "inbox_messages",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_failed_at",
            schema: "sales",
            table: "inbox_messages",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "last_failure_code",
            schema: "sales",
            table: "inbox_messages",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_redriven_at",
            schema: "sales",
            table: "inbox_messages",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "quarantined_at",
            schema: "sales",
            table: "inbox_messages",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<long>(
            name: "redrive_count",
            schema: "sales",
            table: "inbox_messages",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<long>(
            name: "revision",
            schema: "sales",
            table: "inbox_messages",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_subscription_key_available_at_received_at",
            schema: "sales",
            table: "inbox_messages",
            columns: ["subscription_key", "available_at", "received_at"],
            filter: "processed_at IS NULL AND quarantined_at IS NULL"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_failure_metadata",
            schema: "sales",
            table: "inbox_messages",
            sql: "(failure_count = 0 AND last_failed_at IS NULL AND last_failure_code IS NULL AND quarantined_at IS NULL) OR (failure_count > 0 AND last_failed_at IS NOT NULL AND last_failure_code IS NOT NULL AND length(btrim(last_failure_code)) > 0)"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_recovery_counts",
            schema: "sales",
            table: "inbox_messages",
            sql: "revision >= 0 AND failure_count >= 0 AND failures_since_redrive >= 0 AND failures_since_redrive <= failure_count AND redrive_count >= 0"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_recovery_state",
            schema: "sales",
            table: "inbox_messages",
            sql: "processed_at IS NULL OR quarantined_at IS NULL"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_inbox_redrive_metadata",
            schema: "sales",
            table: "inbox_messages",
            sql: "(redrive_count = 0 AND last_redriven_at IS NULL) OR (redrive_count > 0 AND last_redriven_at IS NOT NULL)"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_inbox_messages_subscription_key_available_at_received_at",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_failure_metadata",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_recovery_counts",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_recovery_state",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_inbox_redrive_metadata",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "failure_count",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "failures_since_redrive",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "last_failed_at",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "last_failure_code",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "last_redriven_at",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "quarantined_at",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(
            name: "redrive_count",
            schema: "sales",
            table: "inbox_messages"
        );

        migrationBuilder.DropColumn(name: "revision", schema: "sales", table: "inbox_messages");

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_subscription_key_available_at_received_at",
            schema: "sales",
            table: "inbox_messages",
            columns: ["subscription_key", "available_at", "received_at"],
            filter: "processed_at IS NULL"
        );
    }
}
