# Hosting: GitHub Pages, Supabase and a free API host

The application is split into a static client, a .NET API and a PostgreSQL database. GitHub Pages can only serve the first; the API and database are hosted separately.

    visitor
      |
      +-- https://just4skii.github.io/break-me-if-you-can/   GitHub Pages (static React build)
      |        |  HTTPS + CORS + X-Sandbox-Token
      |        v
      +-- https://<api-host>                                 ASP.NET Core API (container)
               |  Npgsql, TLS, session pooler
               v
          Supabase PostgreSQL (arena schema)

## GitHub Pages - the client

- URL: <https://just4skii.github.io/break-me-if-you-can/>
- Workflow: <code>.github/workflows/pages.yml</code>
- Build: <code>VITE_BASE_PATH=/break-me-if-you-can/</code> and <code>VITE_API_BASE_URL=&lt;api origin&gt;</code>
- Deep links: <code>404.html</code> redirects unknown project paths to the shell, which restores the clean URL; <code>BrowserRouter</code> uses the Vite base as its basename.
- Without <code>API_BASE_URL</code> the client renders an explicit static-preview notice. It does not fake proof or arena results.

Set the repository variable <code>API_BASE_URL</code> (Settings -> Secrets and variables -> Actions -> Variables) to the API origin, then re-run the Pages workflow.

## Supabase - the database

The project is in Frankfurt (<code>eu-central-1</code>). The direct database host is IPv6-only; use the IPv4 session pooler:

| Item | Value |
| --- | --- |
| Pooler host | <code>aws-1-eu-central-1.pooler.supabase.com</code> |
| Port | <code>5432</code> (session mode) |
| Database | <code>postgres</code> |
| Username | <code>postgres.&lt;project-ref&gt;</code> |
| TLS | <code>SSL Mode=Require</code> |

The .NET service uses the database password directly. It does not need the Supabase publishable, secret or service-role API keys, and those must not be placed in the API host. The application owns the <code>arena</code> schema; PostgREST exposes <code>public</code> by default, so do not add <code>arena</code> to the exposed schemas.

    ConnectionStrings__Postgres=Host=aws-1-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project-ref>;Password=<password>;SSL Mode=Require;Timeout=15

Startup runs EF Core migrations behind an advisory lock, so use session mode (5432) or a direct connection for <code>Database__AutoMigrate=true</code>. The transaction pooler (6543) does not preserve session state and must not be used for startup migrations unless migrations are disabled and run as a separate one-off job.

Free-plan projects pause after seven days of low database activity. Point the API host health check at <code>/readyz</code> (it queries the database) so normal demo traffic keeps the project awake.

The schema has been migrated in this project: five <code>arena</code> tables, the non-negative balance check, the double-entry constraints, the balance trigger and one migration history row. A real ten-transfer pessimistic run completed with a balanced ledger.

## Free API host options

| Host | Free tier | .NET container | Observed image digest | Trade-off |
| --- | --- | --- | --- | --- |
| Render | 750 instance hours, 512 MB, spins down after 15 minutes | Yes, Docker | No confirmed platform-resolved digest endpoint | Easiest; cold starts; <code>/proof</code> runtime checks stay UNKNOWN |
| Google Cloud Run | Monthly free allowance, scale to zero | Yes | Yes - the service spec carries the image digest | Requires a billing account; needs a Cloud Run runtime probe for VERIFIED |
| Fly.io | No meaningful free tier | Yes | Yes - top-level <code>image_ref.digest</code> | The existing verified path; paid |
| Koyeb | Current pricing lists paid plans | Yes | Unconfirmed | Not recommended as a free tier |
| Azure App Service F1 | Free, 60 CPU-minutes/day | Custom containers are limited on F1 | No | .NET 10 runtime availability must be checked |

Recommendation: use Render's free Docker service for a usable demo now, and accept that the runtime digest is UNKNOWN because Render does not expose a platform-resolved digest. If a VERIFIED release matters more than the free tier, use Cloud Run (free allowance with billing enabled) and add a Cloud Run probe, or use the existing Fly path.

The CI deploy job and <code>IRuntimeImageProbe</code> are Fly-specific today. A Render deployment that builds from source also produces a different image from the attested GHCR digest, so the four-digest chain would compare unrelated images. Prefer deploying the prebuilt <code>ghcr.io/just4skii/break-me-if-you-can@sha256:...</code> digest (make the package public or configure registry credentials), and add a host-specific probe before claiming runtime verification.

## API environment

| Variable | Purpose |
| --- | --- |
| <code>ConnectionStrings__Postgres</code> | Supabase session-pooler connection string |
| <code>Cors__AllowedOrigins__0</code> | <code>https://just4skii.github.io</code> |
| <code>Proof__Repository</code> | <code>Just4Skii/break-me-if-you-can</code> |
| <code>Proof__GitHubToken</code> | Read-only token for Actions artifacts and attestations |
| <code>Database__AutoMigrate</code> | <code>true</code> on the first deploy; <code>false</code> after a separate migration |
| <code>ASPNETCORE_URLS</code> | The port the host expects, for example <code>http://+:10000</code> on Render |
| <code>Runtime__KillSwitch</code> | <code>true</code> stops writes while keeping reads available |
| <code>Proof__Fly__AppName</code>, <code>Proof__Fly__ApiToken</code> | Only when hosting on Fly |

The sandbox identity crosses origins in <code>X-Sandbox-Token</code> because third-party cookies are blocked by several browsers. CORS is restricted to the configured origins; the API allows the <code>X-Sandbox-Token</code> and <code>Authorization</code> headers.

## Going live checklist

1. Choose the API host and provide the account/token or explicit approval to create it.
2. Deploy the API with the environment above; confirm <code>/healthz</code> and <code>/readyz</code>.
3. Set <code>API_BASE_URL</code> to the API origin and re-run the Pages workflow.
4. Open the Pages site: proof, arena and leaderboard must load real data.
5. Check <code>/proof</code>: on a host without a runtime probe it must say UNKNOWN, not VERIFIED.

## Security notes

- No secret is baked into the Pages build; <code>VITE_*</code> values are public.
- The Supabase database password and GitHub token live only in the API host secret store.
- The sandbox token is stored in <code>localStorage</code>; it only grants access to an anonymous, throwaway sandbox. The API CSP does not apply to the Pages origin.
- Rate limits and the global concurrency cap apply to both same-origin and cross-origin callers.
