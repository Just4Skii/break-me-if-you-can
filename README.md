# Break Me If You Can

[![CI](https://github.com/Just4Skii/break-me-if-you-can/actions/workflows/ci.yml/badge.svg)](https://github.com/Just4Skii/break-me-if-you-can/actions/workflows/ci.yml) [![License: MIT](https://img.shields.io/badge/license-MIT-e3a84e.svg)](LICENSE)

An interactive engineering proof lab. Verify a release from four independent sources, then put the ledger under real concurrent load and watch the database hold.

This is not a dashboard mock-up. The numbers on the proof page are produced by the CI run for the commit that is actually deployed, and the arena executes real PostgreSQL transactions against real rows.

---

## Start here — 2 minute walkthrough

1. **Open `/proof`.** Read the verdict word first: `"Verified"`, `"Failed"` or `"Unknown"`. Below it, the digest chain shows the four values that have to agree before a release can be verified: the built image, the attested image, the digest the deployment asked Fly to run, and the digest Fly reports as running. A green page is impossible if any of them is missing or different.
2. **Read the required checks.** Eighteen checks are listed with their state and the exact observation behind it. Anything that could not be observed says so. Missing evidence is never shown as a pass.
3. **Copy the `gh attestation verify` command** from the release artifact section and run it. It verifies the image against this repository and the exact digest, using GitHub rather than this page. The page also gives you a link to the workflow run, the commit and the Fly Machines API record.
4. **Open `/arena`.** Leave the strategy on optimistic and press *Run 50 transfers*. The receipt log shows accepted requests, rejected requests, retries, PostgreSQL xmin conflicts, latency and the resulting balances. The integrity panel is computed by SQL over the ledger, not by the transfer code.
5. **Switch to pessimistic and run again.** The comparison table appears. Optimistic control pays in retries and conflicts; pessimistic control pays in lock wait time. The final balances must be identical.
6. **Open `/tour`.** Every claim links to the file that implements it, pinned to the deployed commit. The architecture, schema, transaction and pipeline diagrams are generated from the same code you just exercised.

That is the whole product: evidence, an experiment, and the map to the code.

---

## Release evidence

The release run at commit `59be9373` produced these values. They are copied from the run, not from a template.

| Field | Value |
| --- | --- |
| Commit | `59be9373c88930317ce9e7781aba202f89ba90a7` |
| Workflow run | https://github.com/Just4Skii/break-me-if-you-can/actions/runs/42369465155 |
| Image | `ghcr.io/just4skii/break-me-if-you-can` |
| Digest | `sha256:9f51dbd17b2af84ed82ae1dd419ea1eb9583d0976de2b6f2aa6c620bcfa3bb6f` |
| Attestations | 2 (SLSA build provenance and the custom quality predicate) |
| Tests | 128 / 128 passed at that commit |
| Coverage | 86.94% line / 73.5% branch (gate 75%) |
| Dependency audit | no known advisories |
| Deployment | **not deployed**: `DEPLOY_ENABLED` is unset and no Fly machine exists |

The proof endpoint was run locally against that exact evidence artifact and the live GitHub attestations API. It reports **UNKNOWN** — 14 of 20 required checks verified — because the requested and observed deployment digests, and the runtime repository, do not exist until a deploy. That is the intended behaviour: the build and its provenance are proven, the runtime is not observed, so the release is not verified.

The runtime-verification correction described in `docs/deployment-verification.md` adds 21 tests; the suite is now 149 (126 unit, 23 integration), verified by the correction's pull-request run [42371375446](https://github.com/Just4Skii/break-me-if-you-can/actions/runs/42371375446).

```
$ curl -s -H 'Accept: application/json' http://localhost:5099/api/proof | jq '{state, summary, built: .image.builtDigest, attested: .image.attestedDigest}'
{
  "state": "unknown",
  "summary": "6 of 20 required checks cannot be verified. Missing evidence is reported as UNKNOWN, never as a pass.",
  "built": "sha256:9f51dbd17b2af84ed82ae1dd419ea1eb9583d0976de2b6f2aa6c620bcfa3bb6f",
  "attested": "sha256:9f51dbd17b2af84ed82ae1dd419ea1eb9583d0976de2b6f2aa6c620bcfa3bb6f"
}
```

The verification command published by that run is:

```bash
gh attestation verify oci://ghcr.io/just4skii/break-me-if-you-can@sha256:9f51dbd17b2af84ed82ae1dd419ea1eb9583d0976de2b6f2aa6c620bcfa3bb6f \
  --repo Just4Skii/break-me-if-you-can \
  --predicate-type https://slsa.dev/provenance/v1
```

---

## What this is

Three experiences, one deployable ASP.NET Core process and one PostgreSQL database.

| Experience | Route | What it demonstrates |
| --- | --- | --- |
| Engineering proof | `/proof` (HTML) · `/api/proof` (JSON) | CI provenance, test and coverage evidence, dependency audit, image digests, attestation and the observed runtime image |
| Race Condition Arena | `/arena` | Double-entry ledger integrity, optimistic `xmin` concurrency, pessimistic `SELECT … FOR UPDATE`, idempotency, isolation and measured contention |
| Code Tour | `/tour` | Architecture, request lifecycle, schema, transaction processing, CI/CD, attestation and ADRs, with source links |

No registration is required. A visitor receives an anonymous sandbox through a protected, HttpOnly cookie.

The initial release deliberately contains exactly these three experiences. Mutation Roulette, a security playground and chaos engineering are not implemented.

---

## Engineering proof: what `/proof` actually does

`/proof` assembles a `ProofSnapshot` at request time and hands it to a deterministic verifier. The verifier never reads a default success value.

### The four sources

| Source | What it is | How it is obtained |
| --- | --- | --- |
| CI evidence manifest | `ci-evidence.json` with tests, coverage, audit, build and image digest | Downloaded from the **GitHub Actions artifact** of the run discovered by the running commit |
| Deployment intent | The digest the deploy job asked Fly to run | `deployment-intent` artifact from the same workflow run |
| Running image | The digest the platform actually runs | **Fly Machines API** `GET /v1/apps/{app}/machines`, reading `image_ref.digest`, authenticated with a read-only token |
| Attestation | Provenance and the custom quality predicate | **GitHub attestations API** for the digest, queried on every request (not trusted from the manifest) |

The commit is compiled into the assembly at build time (`AssemblyMetadata("CommitSha")`). It is **not** read from an environment variable: an operator could set an environment variable to match the evidence, which would make the whole exercise meaningless.

### The verdict rules

| Condition | State |
| --- | --- |
| Every required check verified | `VERIFIED` |
| Any required check failed, any digest disagrees, attestation missing or bound to the wrong repository/commit/workflow, failed tests, coverage below 75%, vulnerable dependency, unhealthy machine | `FAILED` |
| A required value was not observed, the evidence is older than 24 hours, the manifest is for another commit, GitHub or Fly could not be queried | `UNKNOWN` |

A missing report, an expired artifact, an unreachable platform or a missing token is `UNKNOWN`. It is never rendered as green. The complete rule set lives in `src/BreakMeIfYouCan.Evidence/Verification/ProofVerifier.cs`.

### The digest chain

`"Built"` → `"Attested"` → `"Requested"` → `"Observed"`

Every adjacent pair is compared, not just the first and the last. The chain is complete only when all four are present and identical. The page shows the short form of each digest; the release artifact section shows the full values so they can be copied into `gh attestation verify`.

### Attestations

The pipeline produces two attestations for the same image digest:

- **SLSA build provenance** (`https://slsa.dev/provenance/v1`) from `actions/attest-build-provenance`.
- **A custom quality predicate** (`https://breakmeifyoucan.dev/attestation/quality/v1`) carrying the test counts, coverage and dependency-audit status for that build.

Both are pushed to the registry and verified with `gh attestation verify` in the pipeline. The runtime queries them again from GitHub, so a manifest that merely *claims* an attestation is not enough.

### Security posture: scan is not assessment

A clean NuGet audit covers known advisories in the resolved package graph. It is not a penetration test, a threat model or a complete security assessment. The proof page says so beside the evidence, and `docs/adr/` records the scope.

---

## CI/CD

One workflow run performs quality verification and release together. Evidence can never cross a workflow run boundary.

```text
quality ──► image ──► attest ──► evidence ──► deploy
   │                                  │
   └── artifacts: TRX, Cobertura,      └── ci-evidence.json (uploaded even on failure)
       NuGet audit
```

### Quality gate (pull requests and main)

1. Restore locked dependencies.
2. `dotnet format --verify-no-changes`.
3. Release build with the commit SHA compiled in.
4. Unit tests with Coverlet and TRX output.
5. PostgreSQL integration tests against a real database (service container, or Testcontainers locally).
6. The React client is type-checked and built with Vite, so a broken client cannot reach an image.
7. Merge the Cobertura reports and enforce **75% line coverage**. A missing `Summary.txt` fails the step.
8. `dotnet list package --vulnerable --include-transitive --format json`; the JSON is parsed and the step fails on any advisory.
9. Upload the quality reports even when a test fails.

### Release (main only)

1. Build the container with Buildx and push to GHCR by tag **and** capture the immutable digest.
2. Attest build provenance and the custom quality predicate for the digest.
3. Verify both attestations with `gh attestation verify`.
4. Generate the canonical `ci-evidence.json` from the real TRX/Cobertura/audit outputs.
5. Enforce the release policy. A missing test report, a missing coverage figure, a failed audit, a missing digest or an unverified attestation fails the run.
6. Deploy with `flyctl deploy --image <repository>@<digest>`.
7. Record the deployment intent as an artifact.
8. Query the Fly Machines API and **fail the run** if the running digest differs from the requested one.

Pull requests never reach the image, attestation or deployment jobs. The separate dependency-review check is opt-in (`DEPENDENCY_REVIEW_ENABLED=true`) because it requires the repository dependency graph; the NuGet audit in the quality gate runs regardless.

### Pinned GitHub Actions

Every action is pinned to a full commit SHA. Versions were resolved from the tags at the time of writing; the SHA is the contract.

| Action | Version | Pinned commit | Release |
| --- | --- | --- | --- |
| `actions/checkout` | v5.1.0 | `fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09` | https://github.com/actions/checkout/releases/tag/v5.1.0 |
| `actions/setup-dotnet` | v4.3.1 | `67a3573c9a986a3f9c594539f4ab511d57bb3ce9` | https://github.com/actions/setup-dotnet/releases/tag/v4.3.1 |
| `actions/setup-node` | v4.4.0 | `49933ea5288caeca8642d1e84afbd3f7d6820020` | https://github.com/actions/setup-node/releases/tag/v4.4.0 |
| `actions/upload-artifact` | v4.6.2 | `ea165f8d65b6e75b540449e92b4886f43607fa02` | https://github.com/actions/upload-artifact/releases/tag/v4.6.2 |
| `actions/download-artifact` | v4.3.0 | `d3f86a106a0bac45b974a628896c90dbdf5c8093` | https://github.com/actions/download-artifact/releases/tag/v4.3.0 |
| `docker/setup-buildx-action` | v3.12.0 | `8d2750c68a42422c14e847fe6c8ac0403b4cbd6f` | https://github.com/docker/setup-buildx-action/releases/tag/v3.12.0 |
| `docker/login-action` | v3.7.0 | `c94ce9fb468520275223c153574b00df6fe4bcc9` | https://github.com/docker/login-action/releases/tag/v3.7.0 |
| `docker/build-push-action` | v6.19.2 | `10e90e3645eae34f1e60eeb005ba3a3d33f178e8` | https://github.com/docker/build-push-action/releases/tag/v6.19.2 |
| `actions/attest-build-provenance` | v3.0.0 | `977bb373ede98d70efdf65b84cb5f73e068dcc2a` | https://github.com/actions/attest-build-provenance/releases/tag/v3.0.0 |
| `actions/attest` | v2.4.0 | `ce27ba3b4a9a139d9a20a4a07d69fabb52f1e5bc` | https://github.com/actions/attest/releases/tag/v2.4.0 |
| `actions/dependency-review-action` | v4.9.0 | `2031cfc080254a8a887f58cffee85186f0e49e48` | https://github.com/actions/dependency-review-action/releases/tag/v4.9.0 |
| `superfly/flyctl-actions/setup-flyctl` | v1.6 | `ed8efb33836e8b2096c7fd3ba1c8afe303ebbff1` | https://github.com/superfly/flyctl-actions/tree/v1 |

---

## Race Condition Arena

The arena is a fixed challenge on two accounts in the visitor's own sandbox. Transfers are executed by the server; the client only chooses the strategy and the load.

### The ledger

- `arena.accounts` stores a `balance_cents` with a database `CHECK (balance_cents >= 0)`.
- `arena.transfers` records the intent, strategy, attempt count and observed conflicts.
- `arena.ledger_entries` is append-only: a trigger rejects `UPDATE` and `DELETE`.
- A **deferred constraint trigger** requires exactly two entries per completed transfer (one debit, one credit) whose signed sum is zero, checked at `COMMIT`.
- A unique index on `(sandbox_id, idempotency_key)` makes duplicate transfers physically impossible.
- A unique index on `(transfer_id, direction)` makes two debits or two credits for one transfer impossible.

### Strategy A — optimistic concurrency

The account row maps PostgreSQL's `xmin` system column to a `uint` EF Core concurrency token. A transfer reads both accounts, writes the new balances and lets the `UPDATE … WHERE xmin = …` predicate detect a concurrent commit. On `DbUpdateConcurrencyException` the transaction is rolled back and retried with bounded exponential backoff and jitter. Attempts and conflicts are persisted on the transfer and returned to the UI.

### Strategy B — pessimistic concurrency

A transaction locks the sandbox accounts with `SELECT … FOR UPDATE` in primary-key order. Ordering is independent of transfer direction, which removes the classic A→B / B→A deadlock window. Writers queue on the row lock; xmin conflicts become impossible, and latency becomes lock wait time.

### What the page measures

Accepted, rejected, replayed, retries, conflicts, p50/p95/max latency, wall time, final balances and an integrity report computed by SQL over the ledger. If the ledger is ever unbalanced, the page says so instead of hiding it.

### Required integration tests

All of these run against a real PostgreSQL:

- 50 concurrent transfers under optimistic control (all accepted, conflicts observed, balanced ledger).
- 50 concurrent transfers under pessimistic control (zero conflicts, identical final balances).
- 50 parallel HTTP transfer requests with invariant checks.
- Idempotent replay, including 8 concurrent requests with the same key.
- Insufficient funds: rejected, rolled back, no ledger entries.
- Optimistic `xmin` conflict detection with two real DbContexts.
- The `CHECK` constraint rejects a negative balance bypassing the application.
- Ledger entries reject `UPDATE`.
- A single-entry transfer is rejected by the deferred trigger at commit.
- Duplicate idempotency keys are rejected by the database.
- Sandbox isolation: one visitor cannot touch another visitor's accounts or balances.
- Reset archives the sandbox and preserves the old ledger.

---

## Running locally

### Prerequisites

- .NET 10 SDK (validated with 10.0.202)
- Node.js 22+ (validated with 24)
- Docker Desktop (for PostgreSQL and the container image)

### 1. Start PostgreSQL

```bash
cp .env.example .env          # set POSTGRES_PASSWORD
docker compose up -d db
```

### 2. Start the API

```bash
set -a; . ./.env; set +a
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=breakme;Username=breakme;Password=$POSTGRES_PASSWORD"
dotnet run --project src/BreakMeIfYouCan.Api/BreakMeIfYouCan.Api.csproj
```

The API listens on http://localhost:5219 (see `Properties/launchSettings.json`) and applies migrations at start-up behind a PostgreSQL advisory lock.

### 3. Start the client

```bash
cd web
npm ci
npm run dev
```

Vite serves http://localhost:5173 and proxies `/api`, `/proof`, `/healthz` and `/readyz` to the API.

### 4. Run the tests

```bash
dotnet test BreakMeIfYouCan.slnx -c Release
```

The integration suite uses Testcontainers by default. Set `TEST_POSTGRES` to a connection string to reuse an existing database instead (the CI workflow does this with a service container).

### 5. Build the container

```bash
docker compose up --build
```

The Dockerfile builds the React bundle and publishes the API with `-p:CommitSha`. The final image runs as the non-root `$APP_UID` user.

---

## Deployment (Fly.io)

Target region: **Johannesburg (`jnb`)**. The deployment path is implemented but **not yet executed**, because it requires a Fly account, a paid machine and secrets. Until it runs, `/proof` correctly reports `UNKNOWN` rather than a green state.

Deployment is gated by the repository variable `DEPLOY_ENABLED=true`, so merging a pull request never provisions paid infrastructure by accident. Once enabled, the pipeline deploys an immutable digest:

```bash
flyctl deploy --app break-me-if-you-can \
  --image ghcr.io/<owner>/break-me-if-you-can@sha256:<digest> \
  --strategy rolling --wait-timeout 300 --yes
```

It then reads `https://api.machines.dev/v1/apps/break-me-if-you-can/machines` and fails unless every started machine reports the expected `image_ref` registry, repository and exact digest. `config.image` is never read. See [docs/deployment-verification.md](docs/deployment-verification.md).

### Required secrets

| Secret | Purpose |
| --- | --- |
| `FLY_API_TOKEN` (GitHub secret) | Deployment from the workflow |
| `Proof__GitHubToken` (Fly secret) | Read-only `Actions:read` token used to download the evidence artifacts and query attestations |
| `Proof__Fly__ApiToken` (Fly secret) | Read-only Fly token used to observe the running machine |

The workflow stages `Proof__Repository` automatically. The two proof tokens are operator-managed and are never committed.

### Operational controls

- **Kill switch**: `Runtime__KillSwitch=true` (Fly secret + restart) makes every `/api/arena` endpoint return `503` while health checks and the proof stay available.
- **Concurrency caps**: `Arena__GlobalConcurrencyLimit` bounds database transactions; `Arena__MaxConcurrentRuns` bounds simultaneous runs.
- **Rate limits**: 240 arena requests/minute, 180 writes/minute, 12 runs/minute per visitor, 120 proof requests/minute per IP.
- **Resource shape**: one `shared-cpu-1x` machine with 512 MB, `min_machines_running = 1`, rolling deploys with a 300 second timeout.
- **Spend**: the smallest Fly machine, GHCR retention, 90-day evidence artifacts and 14-day test reports. Set a Fly organisation spending limit before the first deploy.

### Health

- `/healthz` — process liveness, includes the compiled commit.
- `/readyz` — PostgreSQL readiness.
- `/api/meta` — commit, environment, runtime and repository.

---

## Security posture

Implemented:

- Input validation and bounded request models on every arena endpoint.
- Anonymous sandbox identity through a Data Protection cookie; no registration, no personal data.
- Rate limiting and a global transaction cap.
- Parameterised SQL via EF Core; raw SQL only where it is required (`FOR UPDATE`, advisory lock, integrity queries).
- Double-entry invariants enforced by the database, including a deferred constraint trigger.
- Security headers: CSP, `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, `Permissions-Policy`, COOP/CORP.
- Safe exception handling: expected failures become problem details; internals stay in the logs.
- Structured JSON logs in production.
- Pinned workflow actions, least-privilege job permissions, digest-based deployment.
- NuGet advisory audit on every quality run.
- No visitor code execution and no per-visitor containers.

The full findings, residual risks and reliability notes are in [docs/security-review.md](docs/security-review.md). The [acceptance criteria](docs/acceptance-criteria.md) map every mandate requirement to its evidence.

Not assessed:

- Penetration testing and adversarial runtime exploitation.
- Sustained denial-of-service load.
- Third-party hosting, registry and GitHub infrastructure.
- Supply-chain integrity beyond reported advisories.

---

## Repository layout

```text
.github/workflows/ci.yml          quality, image, attest, evidence, deploy
src/BreakMeIfYouCan.Api/
  Arena/                          ledger, strategies, run orchestration, endpoints
  Data/                           EF Core model and the initial migration with triggers
  Proof/                          evidence assembly, Fly probe, attestation probe
  Health/                         liveness, readiness, advisory-locked migrations
  Infrastructure/                 commit metadata, cookie identity, kill switch, headers
src/BreakMeIfYouCan.Evidence/     manifest model, TRX/Cobertura/NuGet parsers, verifier
tools/BreakMeIfYouCan.EvidenceGenerator/   CI evidence CLI and policy gate
tests/BreakMeIfYouCan.Tests.Unit/          evidence rules and parsers
tests/BreakMeIfYouCan.Tests.Integration/   real PostgreSQL concurrency and integrity
web/                                      React + TypeScript + Vite client
docs/adr/                                 architecture decision records
```

---

## Architecture decisions

| ADR | Decision |
| --- | --- |
| [001](docs/adr/001-single-api-project.md) | One ASP.NET Core project with feature folders; no microservices |
| [002](docs/adr/002-database-enforced-ledger.md) | The database enforces the ledger invariants |
| [003](docs/adr/003-generated-evidence.md) | Evidence is generated, never typed |
| [004](docs/adr/004-four-digest-verification.md) | Four digests from four independent sources |
| [005](docs/adr/005-evidence-from-artifacts.md) | Evidence is fetched from the Actions artifacts, not baked into the image |
| [006](docs/adr/006-anonymous-sandboxes.md) | Anonymous sandboxes; reset archives instead of rewriting history |
| [007](docs/adr/007-pinned-dependencies.md) | The NuGet graph and the container base images are pinned |

---

## Status and limits

Verified locally:

- Release build with warnings as errors and a formatting gate.
- 149 automated tests (126 unit, 23 PostgreSQL integration) all passing; the last release run recorded 128 before the runtime-verification correction.
- Coverage 86.94% line / 73.5% branch against the 75% gate.
- A clean NuGet advisory audit.
- A Docker image build of the API plus the React bundle.
- NuGet restore with `--locked-mode` from committed `packages.lock.json` files; base images pinned by digest.
- A container build from the pinned Dockerfile.
- `actionlint` clean on the workflow.

Not yet verified, and therefore not claimed:

- A GitHub Actions run: the first main run completed successfully (quality, image, attestation and evidence).
- A Fly.io deployment, running image digest and live attestation (requires paid infrastructure and secrets).
- The `gh attestation verify` command against a real published image.

See `PROJECT_STATUS.md` for the current state and the exact blockers.

---

## License

MIT. See `LICENSE`.
