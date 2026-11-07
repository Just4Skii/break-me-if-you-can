# Deployment verification record

- **Date:** 2026-11-07
- **Status:** API and region verified from primary documentation. Live deployment not executed (approval boundary).

## 1. Johannesburg region

Fly's region reference lists:

| Code | Location |
| --- | --- |
| `jnb` | Johannesburg, South Africa |

Source: <https://docs.fly.io/reference/regions/> (retrieved 2026-11-07). The `jnb` code is used by `fly.toml` as `primary_region`.

This confirms the region code exists and is documented. It does **not** confirm that this project has an app or a machine there: provisioning requires an account, a payment method and an explicit deployment.

## 2. Machines API

The Machines resource reference documents:

- **Endpoint:** `GET /v1/apps/{app_name}/machines`
- **Authentication:** `Authorization: Bearer ${FLY_API_TOKEN}`
- **Response:** an array of Machine objects including `id`, `state`, `region`, `created_at`, `checks`, `config` and `image_ref`.

`image_ref` is the platform's resolved image description and includes:

~~~json
"image_ref": {
  "registry": "registry-1.docker.io",
  "repository": "rebelthor/sleep",
  "tag": "latest",
  "digest": "sha256:597c3e12f830132be2aa69b4c0deccb0657ea4253e6d59c6f38e41e9f69a0add"
}
~~~

Source: <https://docs.fly.io/machines/api/machines-resource.md> (retrieved 2026-11-07).

## 3. How the platform uses it

| Concern | Implementation |
| --- | --- |
| Machine selection | `Proof:Fly:MachineId` when configured, otherwise **all** machines in `state: started`; a configured id that is absent makes the observation unavailable |
| Running digest | `image_ref.digest` only; `config.image` is never read |
| Health | `state: started` is healthy; `stopped`, `suspended`, `failed` and `destroyed` are unhealthy; transitional states are unknown |
| Provenance | The Machines API URL and observation timestamp are reported on `/proof` |
| CI verification | The deploy job reads the same fields with `jq` and fails unless every started machine reports the expected registry, repository and the exact requested digest |

`config.image` is deliberately never read: it is the request, not the resolved image. `image_ref.digest` is the only accepted runtime evidence.

Additional rules enforced by the probe and the CI verification:

- A configured `Proof:Fly:MachineId` that is not present makes the observation unavailable; the probe never falls back to another machine.
- Zero machines in the `started` state makes the observation unavailable.
- Every started machine must report the same registry, repository and digest. Disagreement makes the observation ambiguous (UNKNOWN).
- The running registry and repository must match the built image reference as well as the digest.
- If `image_ref.digest` is empty, the single-machine endpoint is queried up to two times in total before the observation is reported as UNKNOWN.

## 4. What is still unverified

The following cannot be asserted without a Fly account and a paid machine:

- An actual `GET /v1/apps/break-me-if-you-can/machines` response for this app.
- That `flyctl deploy --image <reference>@<digest>` records the expected `image_ref.digest`.
- That the runtime digest equals the built, attested and requested digests.

The fixtures used by the tests are **hand-written**. They reproduce the documented shape, including a deliberately conflicting `config.image`, but they are not a capture of this app's real response. After the first approved deployment, one real response should be redacted and saved as a fixture so the assumed external-registry form (`registry: ghcr.io`, `repository: owner/repo`, `digest: sha256:...`) is confirmed against the platform.

What has been verified instead:

- The documented response shape matches the parser, and unit tests exercise `image_ref.digest`, missing/empty digests, a missing configured machine, multiple started machines (agreeing and disagreeing), and a deliberately conflicting `config.image`.
- The CI verification step parses `image_ref.digest` and compares it with the deployed digest.
- The deployed digest is the same value used by the attestation and the evidence manifest.

## 5. Commands for the approved deployment

~~~bash
# One-time provisioning (requires approval)
flyctl auth login
flyctl apps create break-me-if-you-can --org <organisation>
flyctl secrets set --app break-me-if-you-can \
  Proof__GitHubToken=<read-only Actions:read token> \
  Proof__Fly__ApiToken=<read-only Fly token>

# Repository settings
#   secret   FLY_API_TOKEN      = Fly deploy token
#   variable DEPLOY_ENABLED     = true

# Then re-run the latest main workflow (or push a merge via a pull request).

# Independent verification afterwards
gh attestation verify \
  oci://ghcr.io/just4skii/break-me-if-you-can@sha256:<digest> \
  --repo Just4Skii/break-me-if-you-can \
  --predicate-type https://slsa.dev/provenance/v1

curl -s -H "Authorization: Bearer $FLY_API_TOKEN" \
  https://api.machines.dev/v1/apps/break-me-if-you-can/machines \
  | jq '.[] | {id, state, region, digest: .image_ref.digest}'
~~~
