using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gloaming.Samples.Wholesale.Sales.Migrations;

/// <inheritdoc />
public partial class AddStockIssueWorkflow : Migration
{
    private static readonly string[] InboxPendingColumns =
    [
        "subscription_key",
        "available_at",
        "received_at",
    ];
    private static readonly string[] OutboxPendingColumns =
    [
        "available_at",
        "lease_until",
        "message_id",
    ];
    private static readonly string[] DeadlineColumns =
    [
        "OrganizationKey",
        "Status",
        "ReplyDeadline",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "sales",
            columns: table => new
            {
                subscription_key = table.Column<string>(type: "text", nullable: false),
                producer_key = table.Column<string>(type: "text", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                message_name = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                tenant_key = table.Column<string>(type: "text", nullable: false),
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
                table.CheckConstraint("ck_inbox_schema", "schema_version > 0");
            }
        );

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "sales",
            columns: table => new
            {
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                destination = table.Column<string>(type: "text", nullable: false),
                message_name = table.Column<string>(type: "text", nullable: false),
                schema_version = table.Column<int>(type: "integer", nullable: false),
                payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                owner_key = table.Column<string>(type: "text", nullable: false),
                correlation_id = table.Column<string>(type: "text", nullable: true),
                causation_id = table.Column<string>(type: "text", nullable: true),
                trace_parent = table.Column<string>(type: "text", nullable: true),
                trace_state = table.Column<string>(type: "text", nullable: true),
                queued_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                available_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false,
                    defaultValueSql: "clock_timestamp()"
                ),
                dispatched_at = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                lease_until = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                attempts = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_outbox_messages", x => x.message_id);
                table.CheckConstraint("ck_outbox_attempts", "attempts >= 0");
                table.CheckConstraint(
                    "ck_outbox_lease",
                    "(lease_token IS NULL) = (lease_until IS NULL)"
                );
                table.CheckConstraint("ck_outbox_schema", "schema_version > 0");
            }
        );

        migrationBuilder.CreateTable(
            name: "stock_issue_requests",
            schema: "sales",
            columns: table => new
            {
                OrganizationKey = table.Column<string>(
                    type: "character varying(256)",
                    maxLength: 256,
                    nullable: false
                ),
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CommandMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                StockPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                ExpectedStockVersion = table.Column<long>(type: "bigint", nullable: false),
                Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                ReplyDeadline = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false
                ),
                Status = table.Column<int>(type: "integer", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                RecordedStockVersion = table.Column<long>(type: "bigint", nullable: true),
                RemainingQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: true
                ),
                Refusal = table.Column<int>(type: "integer", nullable: true),
                AvailableQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                RequestedQuantity = table.Column<decimal>(type: "numeric", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_stock_issue_requests", x => new { x.OrganizationKey, x.Id });
                table.CheckConstraint(
                    "valid_request",
                    "\"Quantity\" > 0 AND \"ExpectedStockVersion\" >= 1 AND \"Version\" >= 1 AND \"Status\" BETWEEN 1 AND 4"
                );
            }
        );

        migrationBuilder.CreateIndex(
            name: "IX_inbox_messages_subscription_key_available_at_received_at",
            schema: "sales",
            table: "inbox_messages",
            columns: InboxPendingColumns,
            filter: "processed_at IS NULL"
        );

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_available_at_lease_until_message_id",
            schema: "sales",
            table: "outbox_messages",
            columns: OutboxPendingColumns,
            filter: "dispatched_at IS NULL"
        );

        migrationBuilder.CreateIndex(
            name: "IX_stock_issue_requests_CommandMessageId",
            schema: "sales",
            table: "stock_issue_requests",
            column: "CommandMessageId",
            unique: true
        );

        migrationBuilder.CreateIndex(
            name: "IX_stock_issue_requests_OrganizationKey_Status_ReplyDeadline",
            schema: "sales",
            table: "stock_issue_requests",
            columns: DeadlineColumns
        );
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "inbox_messages", schema: "sales");

        migrationBuilder.DropTable(name: "outbox_messages", schema: "sales");

        migrationBuilder.DropTable(name: "stock_issue_requests", schema: "sales");
    }
}
