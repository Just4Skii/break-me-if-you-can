# Security and reliability review

- **Date:** 2026-11-07
- **Scope:** the initial release: ASP.NET Core API, PostgreSQL schema, React client, CI/CD pipeline, deployment configuration.
- **Method:** code review, database review, 159 automated tests, CI execution, manual 50-transfer runs, secret scanning, `actionlint`, container build, and inspection of the live attestations API.

This is a self-review. It is **not** a penetration test, a threat model of the hosting provider, or a complete security assessment.

## Trust boundaries

~~~text
Anonymous browser ──cookie──► API process ──SQL──► PostgreSQL
                                  │
                                  ├──► GitHub API (evidence artifact, attestations)  [read-only token]
                                  └──► Fly Machines API (running image)              [read-only token]

GitHub Actions ──digest──► GHCR ──attestation──► GitHub attestations
        └──────────── deploy by digest ───────────► Fly.io
~~~

The runtime treats the CI artifact as untrusted input: it re-validates the commit, run id, repository, workflow, digests and the live attestation before reporting a verified release.

## Findings

| ID | Severity | Finding | Status | Evidence / residual |
| --- | --- | --- | --- | --- |
| S-01 | Medium | The runtime trusts the CI-generated test and coverage figures. A compromised workflow could attest false quality. | Accepted | The attestation binds the image digest to the repository, commit and workflow, and the runtime re-queries it. Proving the *quality numbers* would require an independent auditor. |
| S-02 | Medium | Two read-only tokens live in the runtime environment (GitHub `Actions:read`, Fly Machines read). | Mitigated | Stored as Fly secrets, never in the repository, never logged. Compromise grants read-only visibility, not deployment. |
| S-03 | Medium | Per-visitor rate limits are keyed on a cookie a bot can discard, obtaining new identities. | Partially mitigated / accepted | Global transaction cap (8), global run cap (2), per-run request cap (50) and 15 second SQL timeout bound the damage. A public deployment should add an edge WAF or Turnstile. |
| S-04 | Low | No anti-CSRF token on mutating endpoints. | Accepted | Cookie is `SameSite=Lax`, endpoints are JSON-only, no CORS policy is enabled. A cross-site form POST does not carry the cookie, so it can only create an unrelated sandbox. |
| S-05 | Low | Sandbox identity cookies are protected by a key ring that is ephemeral inside the container. | Accepted | Invalid cookies produce a fresh sandbox; this is an availability/UX property, not an escalation. |
| S-06 | Low | `/proof` publishes the commit, workflow URL, machine id, region and image digests. | Accepted | These are the product; no secret or personal data is exposed. |
| S-07 | Medium | Supply chain: base images, NuGet graph, npm packages, actions. | Mitigated with residual | Actions pinned to commit SHAs; NuGet `packages.lock.json` + `--locked-mode`; npm lockfile; base images pinned by digest; image attestations; advisory audit. Residual: `apt-get install curl` in the runtime stage is not version-pinned, and there is no SBOM or CodeQL job. |
| S-08 | Low | Container privileges. | Mitigated | Non-root `$APP_UID`, no Docker socket, no host mounts, no arbitrary visitor code execution, no per-visitor containers. |
| S-09 | Low | Log contents can include visitor-supplied values. | Accepted | Structured JSON logs escape values; no secrets or cookies are logged. |
| S-10 | Low | Migrations run at start-up. | Accepted | A PostgreSQL advisory lock serialises them; a failed migration fails the process rather than serving a half-migrated schema. Single machine. |
| S-11 | Low | Sandbox rows and ledger history accumulate; no retention job. | Accepted | No personal data. A production run should add a scheduled archive/retention job. |
| S-12 | Low | `/proof` depends on GitHub and Fly; a slow dependency can slow the first request. | Mitigated | 20 second GitHub timeout, 5 second Fly timeout, 60 second report cache, and every failure degrades to UNKNOWN with a note. |

## Controls present

- Input validation and bounded request models on every arena endpoint.
- Database `CHECK` constraints, unique constraints and deferred/append-only triggers.
- Rate limiting per visitor and global concurrency/run caps.
- Security headers: CSP (`script-src 'self'`), `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, COOP/CORP.
- Safe exception handling: expected failures become RFC 7807 problem details; details stay in logs.
- Structured JSON logs in production.
- Health (`/healthz`) and readiness (`/readyz`) probes.
- Deployment kill switch (`Runtime__KillSwitch`) returning 503 on arena routes while the proof stays available; covered by two integration tests.
- Digest-based deployment with independent runtime verification.
- Secret scanning of the tracked tree found only the placeholder `.env.example`.

## Reliability findings

| ID | Finding | Status |
| --- | --- | --- |
| R-01 | Single machine and single PostgreSQL instance: no high availability, no documented backup/restore drill. | Accepted for the demo; a production deployment must add backups and define an RPO/RTO. |
| R-02 | Rate limiting and caching are in-process; they reset on restart and do not coordinate across machines. | Accepted while the app runs one machine. |
| R-03 | Proof correctness depends on external APIs. | By design: unavailability produces UNKNOWN, never a fabricated success. |
| R-04 | A long migration blocks start-up. | Accepted; migrations are small and advisory-locked. |
| R-05 | No circuit breaker on GitHub/Fly calls. | Ameliorated by timeouts and the report cache; noted for future work. |

## Verification performed

- 159 automated tests (130 unit, 29 real-PostgreSQL integration), including concurrency, rollback, idempotency, isolation and database-constraint violation attempts.
- Every pull-request quality run and the main release runs listed in PROJECT_STATUS.md passed, including both attestations.
- `gh attestation verify` executed in CI for SLSA provenance and the custom quality predicate.
- The attestations API was queried independently and the DSSE payload inspected (subject digest, commit, workflow, run).
- `actionlint` clean; workflow permissions reviewed job by job.
- Secret scan: no credentials in the tracked tree.
- Container build validated locally and in CI; runtime runs as a non-root user.

## Limitations

- No penetration test, fuzzing or adversarial exploitation.
- No sustained load or denial-of-service test beyond the 50-request challenge.
- No automated browser end-to-end tests; the client was verified manually at 1440 px and 390 px.
- Hosting, registry and GitHub infrastructure are outside this review.
- A clean dependency audit is not a security assessment.
