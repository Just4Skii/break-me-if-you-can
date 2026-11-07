# ADR 004 — Four digests from four independent sources

- **Status**: accepted
- **Date**: 2026-11-07

## Context

A container reference such as `ghcr.io/owner/repo:latest` is mutable. Proving that the deployed image is the reviewed build requires comparing immutable values, and it requires at least one value that the deployment cannot set for itself.

## Decision

Compare four digests:

1. **Built** — the digest emitted by the image build job.
2. **Attested** — the subject digest recorded in the GitHub attestation.
3. **Requested** — the digest recorded in the deployment intent artifact by the deploy job.
4. **Observed** — the digest reported by the Fly Machines API in `image_ref.digest` for the running machine (`config.image` is the request and is never used as evidence).

Every adjacent pair is compared. `VERIFIED` requires all four to be present and identical, plus an attestation that validates against the expected repository, commit and workflow.

The observed digest is obtained from the Fly Machines API at request time. A deployment environment variable is explicitly **not** evidence of the running image: an operator can set it to anything.

## Consequences

- If Fly cannot be queried, the result is `UNKNOWN`, not green.
- If the attestation is missing, bound to another commit or another workflow, the result is `FAILED`.
- The page exposes the exact `gh attestation verify` command so the visitor can reproduce the check without trusting the page.
- The runtime needs two read-only tokens (GitHub Actions/attestations and Fly Machines). Without them the proof degrades honestly to `UNKNOWN`.

## Alternatives considered

- **Trust the CI manifest's digest only.** Rejected: it proves what was built, not what is running.
- **Read `FLY_IMAGE_REF` or a similar environment variable.** Rejected for the reason above.
- **Compare only built vs observed.** Rejected: it hides a broken attestation chain.
