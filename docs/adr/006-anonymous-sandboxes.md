# ADR 006 — Anonymous sandboxes; reset archives instead of rewriting history

- **Status**: accepted
- **Date**: 2026-11-07

## Context

Visitors must be able to try the arena without registration, and the ledger must remain append-only for the integrity claims to mean anything. A naive "reset" would delete transfers and entries, contradicting the append-only trigger.

## Decision

- A visitor receives a visitor id in a Data Protection-protected, HttpOnly, SameSite=Lax cookie.
- The active sandbox is unique per visitor (partial unique index on `visitor_id WHERE archived_at IS NULL`).
- The sandbox owns exactly two accounts (A and B) with a fixed initial balance.
- `POST /api/arena/reset` archives the current sandbox and creates a new one. The old ledger is never modified.
- Accounts are always resolved from the visitor's own sandbox; account ids supplied by the client are validated against it.

## Consequences

- No registration and no personal data.
- Ledger history survives a reset, which is what lets the integrity claim be tested honestly.
- Abuse is bounded by rate limits, a process-wide transaction cap and per-run request limits.
- Cookie keys are per machine and ephemeral in the container; a visitor landing on another machine gets a fresh sandbox. For a single-machine demo this is acceptable and documented.

## Alternatives considered

- **Delete and recreate the sandbox rows on reset.** Rejected: it would require bypassing the append-only trigger and would destroy the evidence.
- **Global shared demo accounts.** Rejected: one visitor could exhaust another visitor's balance and the privacy story would be worse.
