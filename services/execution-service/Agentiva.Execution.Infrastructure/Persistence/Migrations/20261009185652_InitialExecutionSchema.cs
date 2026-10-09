using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentiva.Execution.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialExecutionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "execution");

            migrationBuilder.CreateTable(
                name: "idempotency_entries",
                schema: "execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    response_payload = table.Column<string>(type: "jsonb", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trading_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trading_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    order_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    limit_price = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    client_order_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    exchange_order_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    trading_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    filled_quantity = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    average_fill_price = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    fee_paid = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    fee_asset = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    rejection_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rejection_detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "execution",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agent_run_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_key",
                schema: "execution",
                table: "idempotency_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inbox_event_consumer",
                schema: "execution",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_created_at",
                schema: "execution",
                table: "orders",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_orders_open_by_symbol_side",
                schema: "execution",
                table: "orders",
                columns: new[] { "symbol", "side", "status" },
                filter: "status IN ('Created', 'Submitted', 'PartiallyFilled')");

            migrationBuilder.CreateIndex(
                name: "ix_orders_trading_intent",
                schema: "execution",
                table: "orders",
                column: "trading_intent_id");

            migrationBuilder.CreateIndex(
                name: "ux_orders_client_order_id",
                schema: "execution",
                table: "orders",
                column: "client_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "execution",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_event_id",
                schema: "execution",
                table: "outbox_messages",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_entries",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "execution");
        }
    }
}
