using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BreakMeIfYouCan.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "players",
                schema: "arena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    username_normalized = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_players", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runs",
                schema: "arena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sandbox_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    strategy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requests = table.Column<int>(type: "integer", nullable: false),
                    concurrency = table.Column<int>(type: "integer", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    pattern = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    idempotency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    accepted = table.Column<int>(type: "integer", nullable: false),
                    rejected = table.Column<int>(type: "integer", nullable: false),
                    replayed = table.Column<int>(type: "integer", nullable: false),
                    total_retries = table.Column<int>(type: "integer", nullable: false),
                    conflicts = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<double>(type: "double precision", nullable: false),
                    score = table.Column<long>(type: "bigint", nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false),
                    integrity_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_runs", x => x.id);
                    table.CheckConstraint("ck_challenge_runs_duration_non_negative", "duration_ms >= 0");
                    table.CheckConstraint("ck_challenge_runs_requests_positive", "requests > 0");
                    table.CheckConstraint("ck_challenge_runs_score_non_negative", "score >= 0");
                    table.ForeignKey(
                        name: "FK_challenge_runs_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "arena",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "player_sessions",
                schema: "arena",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_player_sessions_players_player_id",
                        column: x => x.player_id,
                        principalSchema: "arena",
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_runs_leaderboard",
                schema: "arena",
                table: "challenge_runs",
                columns: new[] { "challenge_key", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_runs_player_created",
                schema: "arena",
                table: "challenge_runs",
                columns: new[] { "player_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_player_sessions_player",
                schema: "arena",
                table: "player_sessions",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ux_player_sessions_token",
                schema: "arena",
                table: "player_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_players_username_normalized",
                schema: "arena",
                table: "players",
                column: "username_normalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge_runs",
                schema: "arena");

            migrationBuilder.DropTable(
                name: "player_sessions",
                schema: "arena");

            migrationBuilder.DropTable(
                name: "players",
                schema: "arena");
        }
    }
}
