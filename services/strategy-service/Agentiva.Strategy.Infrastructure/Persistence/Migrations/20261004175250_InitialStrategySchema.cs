using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentiva.Strategy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialStrategySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "strategy");

            migrationBuilder.CreateTable(
                name: "idempotency_entries",
                schema: "strategy",
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
                schema: "strategy",
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
                schema: "strategy",
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
                name: "signals",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strategy_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    strategy_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    symbol = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    action = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    take_profit = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    reason_codes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "strategies",
                schema: "strategy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    current_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strategies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_key",
                schema: "strategy",
                table: "idempotency_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inbox_event_consumer",
                schema: "strategy",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "strategy",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_event_id",
                schema: "strategy",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_signals_computed_at",
                schema: "strategy",
                table: "signals",
                column: "computed_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_signals_strategy",
                schema: "strategy",
                table: "signals",
                column: "strategy_id");

            migrationBuilder.CreateIndex(
                name: "ix_signals_symbol_computed_at",
                schema: "strategy",
                table: "signals",
                columns: new[] { "symbol", "computed_at" });

            migrationBuilder.CreateIndex(
                name: "ux_strategies_name",
                schema: "strategy",
                table: "strategies",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_entries",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "signals",
                schema: "strategy");

            migrationBuilder.DropTable(
                name: "strategies",
                schema: "strategy");
        }
    }
}
