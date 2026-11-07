# Acceptance criteria

Every criterion from the mandate, with the evidence that supports it. A criterion that is not demonstrated is marked as such.

| # | Criterion | Status | Evidence |
| --- | --- | --- | --- |
| 1 | Reproducible builds | Met with a documented residual | NuGet `packages.lock.json` for all five projects and `--locked-mode` restores in CI and Docker; `npm ci` from the lockfile; base images pinned by digest (`Dockerfile`); deterministic .NET build. Residual: `apt-get install curl` is not version-pinned, so the OS layer is not hermetic. See ADR 007. |
| 2 | Passing automated tests | Met | 151 tests: 126 unit + 25 PostgreSQL integration (the last release run before the runtime-verification and cross-origin corrections recorded 128). Local run and CI run [42369465155](https://github.com/Just4Skii/break-me-if-you-can/actions/runs/42369465155). |
| 3 | Enforced coverage threshold | Met | Merged Cobertura reports: 86.94% line / 73.5% branch against a 75% line gate. A missing `Summary.txt` fails the step. |
| 4 | Parsed dependency audit evidence | Met | `dotnet list package --vulnerable --include-transitive --format json` parsed by the generator; clean; a failed or missing audit fails the release policy. |
| 5 | Valid image attestations | Met | SLSA build provenance and the custom quality predicate, pushed to GHCR and verified with `gh attestation verify` in CI. The attestations API was queried independently and its DSSE payload inspected. |
| 6 | Commit-bound evidence | Met | The running commit is compiled into the assembly; the manifest commit and run id are validated against the discovered run. Local proof against the real artifact verified the binding. |
| 7 | Independently verified runtime digest | **Not demonstrated** | Blocked on a Fly deployment. The probe (image_ref only, repository comparison, all-started-machines agreement, configured-machine strictness) and its tests exist; there is no running machine to observe. `/proof` correctly reports UNKNOWN until then. |
| 8 | Correct transaction integrity | Met | Double-entry writes in one transaction; `CHECK` balance >= 0; unique `(sandbox, idempotency key)` and `(transfer, direction)`; append-only and deferred balance triggers. Tests attempt each violation directly with SQL. |
| 9 | Successful concurrent integration tests | Met | 50 concurrent transfers under optimistic control (all accepted, conflicts observed, balanced) and under pessimistic control (zero conflicts, identical balances); 50 parallel HTTP requests with invariant checks. |
| 10 | Idempotent requests | Met | Replay returns the original transfer; 8 concurrent requests with one key create exactly one transfer; a rejected transfer replays as rejected. |
| 11 | Tested sandbox isolation | Met | A second visitor cannot spend another visitor's accounts (403); balances and ledgers are per sandbox; reset archives rather than deletes. |
| 12 | Working frontend interactions | Met (manual) | The proof page, arena run/strategy switch and tour were driven in a real browser at 1440 px and 390 px. Automated browser E2E tests are a documented gap. |
| 13 | Accurate source-code references | Met | The tour links to files at the running commit; `/api/meta` supplies the commit and repository. Links are file-level, not line-level. |
| 14 | Complete technical documentation | Met | README with the two-minute walkthrough, 7 ADRs, [security review](security-review.md), [deployment verification](deployment-verification.md), [PROJECT_STATUS](../PROJECT_STATUS.md). |

## Reproduce the evidence

~~~bash
# Tests and coverage
dotnet restore BreakMeIfYouCan.slnx --locked-mode
dotnet test BreakMeIfYouCan.slnx -c Release --collect:"XPlat Code Coverage" --results-directory artifacts/coverage-raw
dotnet tool restore
dotnet tool run reportgenerator -reports:"artifacts/coverage-raw/**/coverage.cobertura.xml" \
  -targetdir:artifacts/coverage -reporttypes:"Cobertura;TextSummary" \
  -assemblyfilters:"+BreakMeIfYouCan.*;-BreakMeIfYouCan.Tests.*" -classfilters:"-*Migrations*;-*Program*"

# Evidence manifest from the real reports
dotnet run --project tools/BreakMeIfYouCan.EvidenceGenerator -c Release -- \
  --context artifacts/ci-context.json \
  --test-results artifacts/test-results --coverage artifacts/coverage \
  --audit artifacts/nuget-audit.json --coverage-threshold 75 \
  --expected-assemblies BreakMeIfYouCan.Tests.Unit,BreakMeIfYouCan.Tests.Integration \
  --output artifacts/evidence/ci-evidence.json

# Attestation (after a release)
gh attestation verify oci://ghcr.io/just4skii/break-me-if-you-can@sha256:<digest> \
  --repo Just4Skii/break-me-if-you-can --predicate-type https://slsa.dev/provenance/v1
~~~

Criterion 7 becomes met only when the Fly deployment is approved and the running digest is observed and compared. Until then the proof endpoint reports UNKNOWN, which is the correct result.
