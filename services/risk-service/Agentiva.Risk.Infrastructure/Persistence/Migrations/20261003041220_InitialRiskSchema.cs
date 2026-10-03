using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Agentiva.Risk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialRiskSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "risk");

            migrationBuilder.CreateTable(
                name: "idempotency_entries",
                schema: "risk",
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
                schema: "risk",
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
                schema: "risk",
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
                name: "risk_checks",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trading_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    risk_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    symbol = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_quantity = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    approved_notional = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    effective_entry_price = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    take_profit = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: true),
                    risk_amount = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    risk_budget = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    binding_constraint = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rejection_codes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    checks_json = table.Column<string>(type: "jsonb", nullable: false),
                    trading_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agent_run_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    evaluated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_checks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "risk_policies",
                schema: "risk",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    max_risk_per_trade_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    max_daily_loss_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    max_portfolio_exposure_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    max_asset_concentration_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    max_open_positions = table.Column<int>(type: "integer", nullable: false),
                    min_confidence_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    max_volatility_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    require_stop_loss = table.Column<bool>(type: "boolean", nullable: false),
                    require_take_profit = table.Column<bool>(type: "boolean", nullable: false),
                    slippage_assumption_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    taker_fee_percent = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    market_data_staleness_threshold = table.Column<TimeSpan>(type: "interval", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_position_notional = table.Column<decimal>(type: "numeric(38,18)", precision: 38, scale: 18, nullable: false),
                    max_position_notional_asset = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_risk_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_idempotency_key",
                schema: "risk",
                table: "idempotency_entries",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_inbox_event_consumer",
                schema: "risk",
                table: "inbox_messages",
                columns: new[] { "event_id", "consumer_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_pending",
                schema: "risk",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" },
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_event_id",
                schema: "risk",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_risk_checks_correlation",
                schema: "risk",
                table: "risk_checks",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_risk_checks_evaluated_at",
                schema: "risk",
                table: "risk_checks",
                column: "evaluated_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_risk_checks_trading_intent",
                schema: "risk",
                table: "risk_checks",
                column: "trading_intent_id");

            migrationBuilder.CreateIndex(
                name: "ux_risk_checks_idempotency_key",
                schema: "risk",
                table: "risk_checks",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_risk_policies_name",
                schema: "risk",
                table: "risk_policies",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_risk_policies_single_default",
                schema: "risk",
                table: "risk_policies",
                column: "is_default",
                unique: true,
                filter: "is_default = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_entries",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "risk_checks",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "risk_policies",
                schema: "risk");
        }
    }
}
