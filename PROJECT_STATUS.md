# Project status

Last updated: 2026-11-07
Branch: `feat/initial-release`
Repository: `Skinny1ne/break-me-if-you-can` (creation and first CI run pending at the time of writing)

## Summary

The initial release is implemented end to end and verified locally. The CI/CD workflow is written, linted with `actionlint` and pinned to immutable action commits, but it has not executed on GitHub yet. The deployment and attestation path is implemented and unit tested, but it has not run against Fly.io because that requires a paid machine, account credentials and explicit approval.

The consequence is stated plainly: until the pipeline runs and a release is deployed, `/proof` returns `UNKNOWN`. That is the correct behaviour, not a defect.

## Stage status

| Stage | State | Evidence |
| --- | --- | --- |
| 1. Repository and foundation | Complete locally, first remote CI run pending | Solution builds in Release; 128 tests; formatting gate clean |
| 2. Engineering evidence | Complete and verified locally | Manifest generator, parsers, policy gate, verifier and `/proof` with 105 unit tests |
| 3. Race Condition Arena | Complete and verified locally | 23 PostgreSQL integration tests, including 50 concurrent transfers under both strategies |
| 4. Attestation and deployment | Implemented, execution blocked | Pinned workflow, attestation steps, Fly Machines API probe, digest comparison, deploy job |
| 5. Code tour and release | Documentation complete, public release pending | README, six ADRs, interactive code tour, PROJECT_STATUS |

## Verified locally

Commands were run from the repository root on Windows with .NET SDK 10.0.202, Node 24 and Docker 29.

```text
dotnet build BreakMeIfYouCan.slnx -c Release                0 warnings, 0 errors
dotnet format BreakMeIfYouCan.slnx --verify-no-changes     clean
dotnet test tests/...Unit                                    105 passed
dotnet test tests/...Integration                              23 passed (7 s, real PostgreSQL)
Coverlet + ReportGenerator                                   86.9% line / 73.4% branch (gate 75%)
dotnet list package --vulnerable --include-transitive         no known advisories
actionlint .github/workflows/ci.yml                           clean
npm run build (web)                                           built; ~321 kB JS, ~45 kB CSS
```

Manual smoke test against the running API:

- 50 concurrent optimistic transfers: all accepted, 109 retries/conflicts observed, balanced ledger.
- 50 concurrent pessimistic transfers: all accepted, zero conflicts, identical final balances.
- Idempotency replay: 50 requests with 5 keys produced 5 transfers and 45 replays.
- Cross-visitor transfer attempt: `403`.
- Ledger integrity queries: net zero, two entries per transfer, no negative balances.

## Blockers

### 1. First GitHub Actions run

**Blocked by:** creating and pushing a public GitHub repository.

**Impact:** the CI/CD pipeline, attestations and the commit-bound evidence artifact are unverified until a run completes. The README CI badge will not resolve before the first run.

**Available action:** create the repository, push `feat/initial-release`, open a pull request and merge once the quality job is green.

### 2. Fly.io deployment and live attestation

**Blocked by:** paid infrastructure, a Fly account and secrets; the mandate requires explicit approval before provisioning or deploying.

**Impact:** `/proof` reports `UNKNOWN` (no running image, no deploy intent, no live attestation). The final digest comparison and `gh attestation verify` cannot be demonstrated against the live URL yet.

**Available alternatives:**

1. Deploy to Fly `jnb` with a `shared-cpu-1x` machine once approved; the pipeline is ready.
2. Demonstrate the proof locally with `Proof:LocalEvidencePath`; the page correctly shows `local-file` and remains `UNKNOWN` for the runtime and attestation dimensions.
3. Deploy to another host and implement an equivalent runtime probe. Not done, because the mandate names Fly.

### 3. Operator-managed proof secrets

The runtime needs two read-only tokens to reach the independent sources:

- `Proof__GitHubToken` — `Actions:read` for the evidence artifacts.
- `Proof__Fly__ApiToken` — read-only for the Machines API.

Without them the proof degrades to `UNKNOWN` by design. They are documented in the README and are never committed.

## Known gaps

- No automated browser tests for the React client. The client is type-checked, built, and manually verified at 1440 px and 390 px, but interaction coverage is not automated.
- No CodeQL, SBOM generation or signed-commit enforcement. The dependency review action runs on pull requests only.
- The concurrency evidence is measured up to 50 requests. There is no sustained load test.
- The Data Protection key ring is per machine and ephemeral in the container. A visitor landing on a different machine receives a new sandbox.
- The free-tier limits of GHCR, Actions minutes and Fly spend are not enforced by code; they are documented as operational controls.

## Next actions

1. Create the GitHub repository and push `feat/initial-release`.
2. Open a pull request; confirm the quality job passes (format, build, unit, integration, coverage, audit).
3. Merge to `main` after review; confirm the release jobs produce the image, both attestations and the evidence manifest.
4. Provision Fly (approval required) and configure the four secrets; run the deploy job.
5. Open `/proof` on the deployed URL and confirm `VERIFIED`, then run the exposed `gh attestation verify` command independently.
6. Record the live run and image URLs in the README.
