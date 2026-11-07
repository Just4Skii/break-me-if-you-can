# ADR 003 — Evidence is generated, never typed

- **Status**: accepted
- **Date**: 2026-11-07

## Context

`/proof` must not be able to show a green state that is not backed by a real build. A hand-written JSON file, a hard-coded string or a dashboard that reads a stale summary would all undermine the central claim of the product.

## Decision

- The workflow produces standard outputs: TRX files, merged Cobertura XML and the JSON from `dotnet list package --vulnerable`.
- `BreakMeIfYouCan.EvidenceGenerator` parses those outputs and writes a canonical `ci-evidence.json`.
- Parsers never invent a value. A missing TRX, an absent coverage figure, a failed audit or an unparsable report is recorded as missing.
- A release policy evaluates the manifest. Missing evidence is a violation, not a pass.
- The manifest is written **before** the policy exit code is returned, so the workflow can upload evidence even when quality failed.
- The same verifier that the CI policy uses is the one the API uses at request time.

## Consequences

- The manifest is reproducible from the build outputs and reviewable as a diff.
- A broken parser fails the release loudly; it cannot silently produce a green result.
- The schema is versioned (`schemaVersion`) and the runtime rejects an unknown version as `UNKNOWN`.
- Changing the manifest shape requires coordinated changes in the generator and the API, enforced by the shared library.

## Alternatives considered

- **A third-party coverage or test dashboard.** Rejected: the claim would be "some service says so". The evidence should be produced by this repository's own pipeline.
- **Parsing the workflow logs.** Rejected: brittle and easy to fake.
