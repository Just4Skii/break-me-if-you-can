# ADR 005 — Evidence is fetched from Actions artifacts, not baked into the image

- **Status**: accepted
- **Date**: 2026-11-07

## Context

Baking `ci-evidence.json` into the container is circular: the manifest contains the image digest, but adding the manifest changes the digest. Options were:

1. Put evidence in the image and omit the digest (weak).
2. Two-phase image build (built image, evidence, final image). The final image's digest differs from the built digest, so the evidence no longer describes the shipped image.
3. Keep evidence outside the image and fetch it at runtime.
4. Mount configuration into the container (Fly has no persistent config mount for this).

## Decision

Keep evidence outside the image:

- The workflow uploads `ci-evidence.json` and `deploy-intent.json` as artifacts on the same workflow run.
- The runtime discovers the **successful** run for its own compiled commit, downloads the artifacts, and validates that the manifest's commit and run id match the discovered run.
- Evidence is cached for 60 seconds.
- A local file (`Proof:LocalEvidencePath`) is supported for development and integration tests and is labelled `local-file` in the report.

The runtime never selects "the most recent run"; it selects the run for its commit. Evidence cannot cross a workflow boundary.

## Consequences

- The image remains immutable and minimal.
- The runtime needs network access to the GitHub API and a token with `Actions:read`. Without it, the result is `UNKNOWN`.
- Artifacts have a retention period (90 days here); expired evidence becomes `UNKNOWN`.
- A failed workflow has no successful run, so a deployment of that commit cannot present verified evidence. That is the intended behaviour.

## Alternatives considered

- **GitHub Release assets.** Public and unauthenticated, but it creates a release per commit and mixes release notes with evidence.
- **A separate evidence service.** More moving parts and a new trust boundary.
