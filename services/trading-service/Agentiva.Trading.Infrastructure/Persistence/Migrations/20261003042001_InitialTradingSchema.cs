using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentiva.Trading.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTradingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trading");

            migrationBuilder.CreateTable(
                name: "idempotency_entries",
                schema: "trading",
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
                schema: "trading",
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
                name: "outbox_messages",
                schema: "trading",
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

            migrationBuilder.CreateTable(
                name: "trading_intents",
                schema: "trading",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trading_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    requested_quantity = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    approved_quantity = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    take_profit = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    confidence_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trading_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    signal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agent_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    risk_check_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_codes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trading_intents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_key",
                schema: "trading",
                table: "idempotency_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inbox_event_consumer",
                schema: "trading",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "trading",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_event_id",
                schema: "trading",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trading_intents_correlation",
                schema: "trading",
                table: "trading_intents",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_trading_intents_created_at",
                schema: "trading",
                table: "trading_intents",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_trading_intents_status",
                schema: "trading",
                table: "trading_intents",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_trading_intents_symbol",
                schema: "trading",
                table: "trading_intents",
                columns: new[] { "symbol", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_trading_intents_idempotency_key",
                schema: "trading",
                table: "trading_intents",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_entries",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "trading");

            migrationBuilder.DropTable(
                name: "trading_intents",
                schema: "trading");
        }
    }
}
