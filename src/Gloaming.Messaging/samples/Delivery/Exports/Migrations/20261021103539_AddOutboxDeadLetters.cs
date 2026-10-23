using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Messaging.Exports.Migrations;

/// <inheritdoc />
public partial class AddOutboxDeadLetters : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_outgoing_work_available_at_lease_until_message_id",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "dead_lettered_at",
            schema: "exports",
            table: "outgoing_work",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<long>(
            name: "failure_count",
            schema: "exports",
            table: "outgoing_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<long>(
            name: "failures_since_redrive",
            schema: "exports",
            table: "outgoing_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_failed_at",
            schema: "exports",
            table: "outgoing_work",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "last_failure_code",
            schema: "exports",
            table: "outgoing_work",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true
        );

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_redriven_at",
            schema: "exports",
            table: "outgoing_work",
            type: "timestamp with time zone",
            nullable: true
        );

        migrationBuilder.AddColumn<long>(
            name: "redrive_count",
            schema: "exports",
            table: "outgoing_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.AddColumn<long>(
            name: "revision",
            schema: "exports",
            table: "outgoing_work",
            type: "bigint",
            nullable: false,
            defaultValue: 0L
        );

        migrationBuilder.CreateIndex(
            name: "IX_outgoing_work_available_at_lease_until_message_id",
            schema: "exports",
            table: "outgoing_work",
            columns: ["available_at", "lease_until", "message_id"],
            filter: "dispatched_at IS NULL AND dead_lettered_at IS NULL"
        );

        migrationBuilder.CreateIndex(
            name: "IX_outgoing_work_destination_dead_lettered_at_message_id",
            schema: "exports",
            table: "outgoing_work",
            columns: ["destination", "dead_lettered_at", "message_id"],
            filter: "dispatched_at IS NULL AND dead_lettered_at IS NOT NULL"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_outbox_failure_metadata",
            schema: "exports",
            table: "outgoing_work",
            sql: "(failure_count = 0 AND last_failed_at IS NULL AND last_failure_code IS NULL AND dead_lettered_at IS NULL) OR (failure_count > 0 AND last_failed_at IS NOT NULL AND last_failure_code IS NOT NULL AND length(btrim(last_failure_code)) > 0)"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_outbox_recovery_counts",
            schema: "exports",
            table: "outgoing_work",
            sql: "revision >= 0 AND failure_count >= 0 AND failures_since_redrive >= 0 AND failures_since_redrive <= failure_count AND failure_count <= attempts AND redrive_count >= 0"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_outbox_recovery_state",
            schema: "exports",
            table: "outgoing_work",
            sql: "(dispatched_at IS NULL OR dead_lettered_at IS NULL) AND ((dispatched_at IS NULL AND dead_lettered_at IS NULL) OR lease_token IS NULL)"
        );

        migrationBuilder.AddCheckConstraint(
            name: "ck_outbox_redrive_metadata",
            schema: "exports",
            table: "outgoing_work",
            sql: "(redrive_count = 0 AND last_redriven_at IS NULL) OR (redrive_count > 0 AND last_redriven_at IS NOT NULL)"
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_outgoing_work_available_at_lease_until_message_id",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropIndex(
            name: "IX_outgoing_work_destination_dead_lettered_at_message_id",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_outbox_failure_metadata",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_outbox_recovery_counts",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_outbox_recovery_state",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropCheckConstraint(
            name: "ck_outbox_redrive_metadata",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "dead_lettered_at",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "failure_count",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "failures_since_redrive",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "last_failed_at",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "last_failure_code",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "last_redriven_at",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(
            name: "redrive_count",
            schema: "exports",
            table: "outgoing_work"
        );

        migrationBuilder.DropColumn(name: "revision", schema: "exports", table: "outgoing_work");

        migrationBuilder.CreateIndex(
            name: "IX_outgoing_work_available_at_lease_until_message_id",
            schema: "exports",
            table: "outgoing_work",
            columns: ["available_at", "lease_until", "message_id"],
            filter: "dispatched_at IS NULL"
        );
    }
}
