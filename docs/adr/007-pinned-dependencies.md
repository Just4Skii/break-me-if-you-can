# ADR 007 — Pin the dependency graph and the base images

- **Status**: accepted
- **Date**: 2026-11-07

## Context

"Reproducible builds" is only true if the inputs are fixed. The direct NuGet versions are centralised in `Directory.Packages.props` and the frontend uses `npm ci`, but transitive NuGet packages resolve from nuget.org at restore time and would drift. Container base images were referenced by tag, which is mutable.

## Decision

- Every .NET project commits a `packages.lock.json`; `RestorePackagesWithLockFile` is enabled in `Directory.Build.props`.
- CI and the Docker build restore with `--locked-mode`. A package change requires regenerating the lock file, otherwise the build fails.
- The Dockerfile references `node:24-alpine`, `mcr.microsoft.com/dotnet/sdk:10.0` and `mcr.microsoft.com/dotnet/aspnet:10.0` by tag **and** digest.
- The frontend is installed with `npm ci` from the committed lock file.

## Consequences

- The restore graph is identical on a developer machine, in CI and in the image build.
- A dependency update is a reviewable diff to `packages.lock.json` rather than an implicit restore resolution.
- Base image digests must be refreshed deliberately (and re-reviewed); a stale pinned image will not receive upstream fixes on its own.
- The runtime stage still installs `curl` from the Debian repositories, so the image is not hermetic at the operating-system layer. This is recorded as a residual reproducibility limit in the security review.

## Alternatives considered

- **Floating tags and no lock files.** Simpler, but "it built on my machine" would be a real risk and the reproducibility claim would be overstated.
- **A fully hermetic build with a pinned OS package snapshot.** Disproportionate for this project; the residual is documented instead of hidden.
