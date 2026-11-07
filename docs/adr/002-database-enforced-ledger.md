# ADR 002 — The database enforces the ledger invariants

- **Status**: accepted
- **Date**: 2026-11-07

## Context

The arena demonstrates a double-entry ledger under concurrency. The implementation must make these statements true even if application code is buggy or bypassed:

1. A completed transfer has exactly one debit and one credit.
2. The signed sum of a transfer's entries is zero.
3. No account balance is negative.
4. A duplicate transfer cannot exist for the same sandbox and idempotency key.
5. Ledger history is append-only.

Application checks alone cannot guarantee any of these: a second code path, a migration, a manual query or a concurrent bug can violate them.

## Decision

Enforce the invariants in PostgreSQL, in the migration that creates the schema:

- `CHECK (balance_cents >= 0)` on `arena.accounts`.
- `CHECK (amount_cents > 0)` and `CHECK (from_account_id <> to_account_id)` on transfers.
- `UNIQUE (sandbox_id, idempotency_key)` where the key is not null.
- `UNIQUE (transfer_id, direction)` on ledger entries.
- `CHECK (direction IN ('debit','credit'))` and `CHECK (amount_cents > 0)`.
- A deferred `CONSTRAINT TRIGGER` that, at `COMMIT`, requires exactly two entries per completed transfer and a zero signed sum.
- A `BEFORE UPDATE OR DELETE` trigger that rejects any mutation of a ledger entry.

The balance update, transfer row and both entries are written in one transaction, so the trigger observes the complete pair.

## Consequences

- An invalid write fails the transaction, not a later reconciliation job.
- The integration suite proves each rule by trying to violate it directly with SQL.
- Deleting a transfer is impossible without disabling the trigger; the application never deletes one. Reset archives the sandbox instead.
- Migrations are the source of truth for integrity objects, and the raw SQL is reviewed in the migration file.

## Alternatives considered

- **Application-level invariants only.** Rejected: the whole point of the arena is that the store holds under concurrency.
- **Event sourcing with projections.** Rejected as disproportionate for two accounts and a fixed challenge.
- **Deferred foreign-key trickery instead of a trigger.** A trigger states the rule directly and produces a useful error message.
