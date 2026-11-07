using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BreakMeIfYouCan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialArena : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "arena");

            migrationBuilder.CreateTable(
                name: "sandboxes",
                schema: "arena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visitor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    initial_balance_cents = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sandboxes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "arena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sandbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    strategy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    conflicts = table.Column<int>(type: "integer", nullable: false),
                    duration_microseconds = table.Column<int>(type: "integer", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.id);
                    table.CheckConstraint("ck_transfers_amount_positive", "amount_cents > 0");
                    table.CheckConstraint("ck_transfers_attempts_positive", "attempts >= 1");
                    table.CheckConstraint("ck_transfers_status", "status IN ('completed', 'rejected')");
                    table.CheckConstraint("ck_transfers_strategy", "strategy IN ('optimistic', 'pessimistic')");
                });

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "arena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sandbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    balance_cents = table.Column<long>(type: "bigint", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_balance_non_negative", "balance_cents >= 0");
                    table.CheckConstraint("ck_accounts_balance_upper_bound", "balance_cents <= 1000000000");
                    table.ForeignKey(
                        name: "FK_accounts_sandboxes_sandbox_id",
                        column: x => x.sandbox_id,
                        principalSchema: "arena",
                        principalTable: "sandboxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "arena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount_positive", "amount_cents > 0");
                    table.CheckConstraint("ck_ledger_entries_direction", "direction IN ('debit', 'credit')");
                    table.ForeignKey(
                        name: "FK_ledger_entries_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "arena",
                        principalTable: "transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_accounts_sandbox_code",
                schema: "arena",
                table: "accounts",
                columns: new[] { "sandbox_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_account",
                schema: "arena",
                table: "ledger_entries",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_transfer_direction",
                schema: "arena",
                table: "ledger_entries",
                columns: new[] { "transfer_id", "direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sandboxes_visitor_active",
                schema: "arena",
                table: "sandboxes",
                column: "visitor_id",
                unique: true,
                filter: "archived_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_sandbox_created",
                schema: "arena",
                table: "transfers",
                columns: new[] { "sandbox_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_transfers_sandbox_idempotency",
                schema: "arena",
                table: "transfers",
                columns: new[] { "sandbox_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            // ------------------------------------------------------------------
            // Integrity that EF's model cannot express. These objects are created
            // here rather than through the model builder so they stay visible in
            // review and cannot be quietly regenerated away.
            // ------------------------------------------------------------------

            migrationBuilder.Sql("ALTER TABLE arena.transfers ADD CONSTRAINT ck_transfers_accounts_differ CHECK (from_account_id <> to_account_id);");

            // A transfer may not leave an account below zero even if application code
            // is bypassed. This is the authoritative overdraft guard.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION arena.assert_transfer_balanced() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                DECLARE
                    entry_count integer;
                    net bigint;
                    transfer_status text;
                BEGIN
                    SELECT t.status INTO transfer_status
                    FROM arena.transfers t
                    WHERE t.id = NEW.transfer_id;

                    IF transfer_status IS NULL THEN
                        RAISE EXCEPTION 'ledger entry % references a missing transfer', NEW.id
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF transfer_status <> 'completed' THEN
                        RAISE EXCEPTION 'ledger entries cannot be attached to a transfer in status %', transfer_status
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    SELECT COUNT(*),
                           COALESCE(SUM(CASE WHEN direction = 'debit' THEN -amount_cents ELSE amount_cents END), 0)
                      INTO entry_count, net
                      FROM arena.ledger_entries
                     WHERE transfer_id = NEW.transfer_id;

                    IF entry_count <> 2 THEN
                        RAISE EXCEPTION 'transfer % must have exactly two ledger entries, found %', NEW.transfer_id, entry_count
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    IF net <> 0 THEN
                        RAISE EXCEPTION 'transfer % is unbalanced: net % cents', NEW.transfer_id, net
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    RETURN NULL;
                END;
                $fn$;
                """);

            // Deferred to COMMIT, so both halves of the entry pair are present when the
            // check runs, and a rolled-back transaction never leaves a trace.
            migrationBuilder.Sql("""
                CREATE CONSTRAINT TRIGGER trg_ledger_entries_balanced
                    AFTER INSERT ON arena.ledger_entries
                    DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW
                    EXECUTE FUNCTION arena.assert_transfer_balanced();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION arena.reject_ledger_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                BEGIN
                    RAISE EXCEPTION 'ledger entries are append-only' USING ERRCODE = 'raise_exception';
                END;
                $fn$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_ledger_entries_append_only
                    BEFORE UPDATE OR DELETE ON arena.ledger_entries
                    FOR EACH ROW
                    EXECUTE FUNCTION arena.reject_ledger_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ledger_entries_append_only ON arena.ledger_entries;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_ledger_entries_balanced ON arena.ledger_entries;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS arena.reject_ledger_mutation();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS arena.assert_transfer_balanced();");

            migrationBuilder.DropTable(
                name: "accounts",
                schema: "arena");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "arena");

            migrationBuilder.DropTable(
                name: "sandboxes",
                schema: "arena");

            migrationBuilder.DropTable(
                name: "transfers",
                schema: "arena");
        }
    }
}