using BreakMeIfYouCan.Api.Competition;
using Microsoft.EntityFrameworkCore;

namespace BreakMeIfYouCan.Api.Data;

public sealed class ArenaDbContext(DbContextOptions<ArenaDbContext> options) : DbContext(options)
{
    public DbSet<Sandbox> Sandboxes => Set<Sandbox>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transfer> Transfers => Set<Transfer>();

    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<Player> Players => Set<Player>();

    public DbSet<PlayerSession> PlayerSessions => Set<PlayerSession>();

    public DbSet<ChallengeRun> ChallengeRuns => Set<ChallengeRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema("arena");

        modelBuilder.Entity<Sandbox>(entity =>
        {
            entity.ToTable("sandboxes");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.ScenarioKey).HasMaxLength(64).IsRequired();
            entity.Property(s => s.CreatedAt).IsRequired();
            entity.HasIndex(s => s.VisitorId)
                .IsUnique()
                .HasFilter("archived_at IS NULL")
                .HasDatabaseName("ux_sandboxes_visitor_active");
            entity.HasMany(s => s.Accounts)
                .WithOne(a => a.Sandbox)
                .HasForeignKey(a => a.SandboxId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("accounts", table =>
            {
                table.HasCheckConstraint("ck_accounts_balance_non_negative", "balance_cents >= 0");
                table.HasCheckConstraint("ck_accounts_balance_upper_bound", "balance_cents <= 1000000000");
            });
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Code).HasMaxLength(8).IsRequired();
            entity.Property(a => a.Label).HasMaxLength(120).IsRequired();
            entity.Property(a => a.BalanceCents).IsRequired();

            // PostgreSQL's xmin system column as an optimistic concurrency token.
            entity.Property(a => a.Version)
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            entity.HasIndex(a => new { a.SandboxId, a.Code })
                .IsUnique()
                .HasDatabaseName("ux_accounts_sandbox_code");
        });

        modelBuilder.Entity<Transfer>(entity =>
        {
            entity.ToTable("transfers", table =>
            {
                table.HasCheckConstraint("ck_transfers_amount_positive", "amount_cents > 0");
                table.HasCheckConstraint("ck_transfers_attempts_positive", "attempts >= 1");
                table.HasCheckConstraint(
                    "ck_transfers_strategy",
                    "strategy IN ('optimistic', 'pessimistic')");
                table.HasCheckConstraint(
                    "ck_transfers_status",
                    "status IN ('completed', 'rejected')");
            });
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Strategy).HasMaxLength(16).IsRequired();
            entity.Property(t => t.Status).HasMaxLength(16).IsRequired();
            entity.Property(t => t.IdempotencyKey).HasMaxLength(120);
            entity.Property(t => t.FailureReason).HasMaxLength(200);
            entity.HasIndex(t => new { t.SandboxId, t.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL")
                .HasDatabaseName("uq_transfers_sandbox_idempotency");
            entity.HasIndex(t => new { t.SandboxId, t.CreatedAt })
                .HasDatabaseName("ix_transfers_sandbox_created");
            entity.HasMany(t => t.Entries)
                .WithOne()
                .HasForeignKey(e => e.TransferId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LedgerEntry>(entity =>
        {
            entity.ToTable("ledger_entries", table =>
            {
                table.HasCheckConstraint("ck_ledger_entries_amount_positive", "amount_cents > 0");
                table.HasCheckConstraint(
                    "ck_ledger_entries_direction",
                    "direction IN ('debit', 'credit')");
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Direction).HasMaxLength(8).IsRequired();
            entity.HasIndex(e => new { e.TransferId, e.Direction })
                .IsUnique()
                .HasDatabaseName("ux_ledger_entries_transfer_direction");
            entity.HasIndex(e => e.AccountId).HasDatabaseName("ix_ledger_entries_account");
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.ToTable("players");
            entity.HasKey(player => player.Id);
            entity.Property(player => player.Username).HasMaxLength(24).IsRequired();
            entity.Property(player => player.UsernameNormalized).HasMaxLength(24).IsRequired();
            entity.Property(player => player.PasswordHash).HasMaxLength(256).IsRequired();
            entity.Property(player => player.CreatedAt).IsRequired();
            entity.HasIndex(player => player.UsernameNormalized)
                .IsUnique()
                .HasDatabaseName("ux_players_username_normalized");
        });

        modelBuilder.Entity<PlayerSession>(entity =>
        {
            entity.ToTable("player_sessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(session => session.CreatedAt).IsRequired();
            entity.Property(session => session.ExpiresAt).IsRequired();
            entity.HasIndex(session => session.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_player_sessions_token");
            entity.HasIndex(session => session.PlayerId).HasDatabaseName("ix_player_sessions_player");
            entity.HasOne(session => session.Player)
                .WithMany(player => player.Sessions)
                .HasForeignKey(session => session.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChallengeRun>(entity =>
        {
            entity.ToTable("challenge_runs", table =>
            {
                table.HasCheckConstraint("ck_challenge_runs_requests_positive", "requests > 0");
                table.HasCheckConstraint("ck_challenge_runs_duration_non_negative", "duration_ms >= 0");
                table.HasCheckConstraint("ck_challenge_runs_score_non_negative", "score >= 0");
            });
            entity.HasKey(run => run.Id);
            entity.Property(run => run.ChallengeKey).HasMaxLength(64).IsRequired();
            entity.Property(run => run.Strategy).HasMaxLength(16).IsRequired();
            entity.Property(run => run.Pattern).HasMaxLength(24).IsRequired();
            entity.Property(run => run.Idempotency).HasMaxLength(16).IsRequired();
            entity.Property(run => run.IntegrityJson).HasColumnType("text").IsRequired();
            entity.Property(run => run.CreatedAt).IsRequired();
            entity.HasIndex(run => new { run.ChallengeKey, run.Score })
                .HasDatabaseName("ix_challenge_runs_leaderboard");
            entity.HasIndex(run => new { run.PlayerId, run.CreatedAt })
                .HasDatabaseName("ix_challenge_runs_player_created");
            entity.HasOne(run => run.Player)
                .WithMany(player => player.Runs)
                .HasForeignKey(run => run.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ApplySnakeCaseColumnNames(modelBuilder);
    }

    /// <summary>
    /// PostgreSQL identifiers are conventionally snake_case, and the raw SQL in this
    /// project (advisory locks, FOR UPDATE, integrity queries) reads better that way.
    /// The xmin concurrency token keeps its system column name.
    /// </summary>
    private static void ApplySnakeCaseColumnNames(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (string.Equals(property.Name, nameof(Account.Version), StringComparison.Ordinal))
                {
                    continue;
                }

                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsUpper(current) && i > 0 && (!char.IsUpper(value[i - 1]) || (i + 1 < value.Length && char.IsLower(value[i + 1]))))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
