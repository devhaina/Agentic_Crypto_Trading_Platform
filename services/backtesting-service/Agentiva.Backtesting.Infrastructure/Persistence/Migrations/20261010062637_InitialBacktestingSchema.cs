using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentiva.Backtesting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialBacktestingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "backtesting");

            migrationBuilder.CreateTable(
                name: "backtest_runs",
                schema: "backtesting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    timeframe = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    strategy_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    strategy_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    starting_capital = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    fee_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    slippage_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    walk_forward_window_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    metrics = table.Column<string>(type: "jsonb", nullable: true),
                    walk_forward_windows = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_entries",
                schema: "backtesting",
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
                schema: "backtesting",
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
                schema: "backtesting",
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
                name: "backtest_trades",
                schema: "backtesting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    backtest_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    entry_time = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    exit_time = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    exit_price = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    exit_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    gross_pnl = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    fees = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    slippage_cost = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    reason_codes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backtest_trades", x => x.id);
                    table.ForeignKey(
                        name: "fk_backtest_trades_backtest_runs_backtest_id",
                        column: x => x.backtest_id,
                        principalSchema: "backtesting",
                        principalTable: "backtest_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_backtest_runs_created_at",
                schema: "backtesting",
                table: "backtest_runs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_backtest_trades_run_entry_time",
                schema: "backtesting",
                table: "backtest_trades",
                columns: new[] { "backtest_id", "entry_time" });

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_key",
                schema: "backtesting",
                table: "idempotency_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inbox_event_consumer",
                schema: "backtesting",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "backtesting",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_event_id",
                schema: "backtesting",
                table: "outbox_messages",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backtest_trades",
                schema: "backtesting");

            migrationBuilder.DropTable(
                name: "idempotency_entries",
                schema: "backtesting");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "backtesting");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "backtesting");

            migrationBuilder.DropTable(
                name: "backtest_runs",
                schema: "backtesting");
        }
    }
}
