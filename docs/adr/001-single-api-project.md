# ADR 001 — One API project with feature folders

- **Status**: accepted
- **Date**: 2026-11-07

## Context

The product has three experiences: release evidence, the concurrency arena and a code tour. They share one database, one anonymous visitor identity and one deployment. The interesting engineering is inside a single transaction boundary, not across a network.

## Decision

Use one ASP.NET Core project with feature-oriented folders:

- `Arena/` — ledger, transfer execution, run orchestration and endpoints.
- `Proof/` — evidence assembly, runtime probes and the `/proof` endpoint.
- `Health/` — liveness, readiness and the migration start-up task.
- `Data/` — the EF Core model and migrations.
- `Infrastructure/` — commit metadata, cookie identity, the kill switch and security headers.

A separate class library holds the evidence manifest, parsers and verifier because the CI CLI uses exactly the same code as the API.

## Consequences

- One container, one health endpoint, one migration path, one log stream.
- Tests can host the real application with `WebApplicationFactory` and exercise routing, cookies, rate limiting and EF Core without stubs.
- The arena and the proof share the request pipeline; a global kill switch and rate limits apply consistently.
- The project will grow, but the separation is by feature, so a future split would follow domain boundaries rather than layers.

## Alternatives considered

- **Microservices** for arena and proof. Rejected: more topology, more failure modes, no additional evidence. A recruiter learns more from a correct transaction than from a service mesh.
- **Clean Architecture with four projects.** Rejected: the interfaces would exist for their own sake. The evidence library is the only genuinely shared component.
