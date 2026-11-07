import type { ReactNode } from 'react'
import { useApi } from '../hooks/useApi'
import type { Meta } from '../types'
import { ArrowUpRightIcon } from '../components/Icons'
import { SectionHead } from '../components/SectionHead'

interface Source {
  path: string
  label: string
}

const SOURCES = {
  program: { path: 'src/BreakMeIfYouCan.Api/Program.cs', label: 'Program.cs' },
  endpoints: { path: 'src/BreakMeIfYouCan.Api/Arena/ArenaEndpoints.cs', label: 'ArenaEndpoints.cs' },
  executor: { path: 'src/BreakMeIfYouCan.Api/Arena/TransferExecutor.cs', label: 'TransferExecutor.cs' },
  sandbox: { path: 'src/BreakMeIfYouCan.Api/Arena/SandboxService.cs', label: 'SandboxService.cs' },
  dbcontext: { path: 'src/BreakMeIfYouCan.Api/Data/ArenaDbContext.cs', label: 'ArenaDbContext.cs' },
  migration: {
    path: 'src/BreakMeIfYouCan.Api/Data/Migrations/20261107095827_InitialArena.cs',
    label: 'InitialArena migration',
  },
  proofService: { path: 'src/BreakMeIfYouCan.Api/Proof/ProofService.cs', label: 'ProofService.cs' },
  flyProbe: { path: 'src/BreakMeIfYouCan.Api/Proof/FlyRuntimeProbe.cs', label: 'FlyRuntimeProbe.cs' },
  attestationProbe: {
    path: 'src/BreakMeIfYouCan.Api/Proof/GitHubAttestationProbe.cs',
    label: 'GitHubAttestationProbe.cs',
  },
  verifier: { path: 'src/BreakMeIfYouCan.Evidence/Verification/ProofVerifier.cs', label: 'ProofVerifier.cs' },
  trx: { path: 'src/BreakMeIfYouCan.Evidence/Parsing/TrxParser.cs', label: 'TrxParser.cs' },
  generator: {
    path: 'tools/BreakMeIfYouCan.EvidenceGenerator/Program.cs',
    label: 'EvidenceGenerator',
  },
  workflow: { path: '.github/workflows/ci.yml', label: 'ci.yml' },
  tests: {
    path: 'tests/BreakMeIfYouCan.Tests.Integration/ArenaConcurrencyTests.cs',
    label: 'ArenaConcurrencyTests.cs',
  },
  readme: { path: 'README.md', label: 'README' },
} satisfies Record<string, Source>

const SECTIONS = [
  { id: 'architecture', index: '01', title: 'Architecture' },
  { id: 'lifecycle', index: '02', title: 'Request lifecycle' },
  { id: 'schema', index: '03', title: 'Database schema' },
  { id: 'transactions', index: '04', title: 'Transaction processing' },
  { id: 'idempotency', index: '05', title: 'Idempotency' },
  { id: 'ci', index: '06', title: 'CI and evidence' },
  { id: 'deployment', index: '07', title: 'Deployment and attestation' },
  { id: 'adr', index: '08', title: 'Decision records' },
  { id: 'history', index: '09', title: 'Commit history' },
]

const ADRS = [
  {
    id: '001',
    title: 'One API project, feature folders',
    decision: 'A single ASP.NET Core project with Proof, Arena and Health folders; no services or message bus.',
    why: 'The interesting engineering is inside one transaction boundary. Splitting it would add topology without adding evidence.',
  },
  {
    id: '002',
    title: 'The database enforces the ledger invariants',
    decision:
      'CHECK balance >= 0, unique (sandbox, idempotency key), unique (transfer, direction), an append-only ledger trigger and a deferred constraint trigger for the zero-sum pair.',
    why: 'Application code can be wrong or bypassed. The invariants belong where the data lives.',
  },
  {
    id: '003',
    title: 'Evidence is generated, never typed',
    decision:
      'TRX, Cobertura and NuGet audit outputs are parsed by a CLI into one canonical manifest; a policy gate blocks release on any missing result.',
    why: 'A dashboard that reads a hand-written JSON file proves nothing.',
  },
  {
    id: '004',
    title: 'Four digests, four independent sources',
    decision:
      'Compare the built digest, the attested subject, the deploy intent and the Fly Machines API observation. Any missing value is UNKNOWN; any disagreement is FAILED.',
    why: 'An environment variable can be set to match anything. The release is only proven when the platform says what is running.',
  },
]

function sourceUrl(meta: Meta | undefined, source: Source, lines?: [number, number]): string | undefined {
  if (!meta?.repositoryUrl) return undefined
  const ref = meta.commit && meta.commit !== 'unknown' ? meta.commit : 'main'
  const anchor = lines ? '#L' + lines[0] + '-L' + lines[1] : ''
  return meta.repositoryUrl + '/blob/' + ref + '/' + source.path + anchor
}

export function TourPage() {
  const metaResult = useApi<Meta>('/api/meta')
  const meta = metaResult.status === 'success' ? metaResult.data : undefined

  const SourceLink = ({ source, lines }: { source: Source; lines?: [number, number] }) => {
    const href = sourceUrl(meta, source, lines)
    if (!href) return <code className="source-static">{source.label}</code>
    return (
      <a className="source-link" href={href} target="_blank" rel="noreferrer">
        <code>{source.label}</code>
        <ArrowUpRightIcon size={11} />
      </a>
    )
  }

  return (
    <article className="tour">
      <header className="tour-head">
        <span className="label">Code tour</span>
        <h1 className="display-l">How this thing is actually built.</h1>
        <p className="tour-intro">
          A walkthrough of the running implementation. Every claim below links to the file that
          implements it, pinned to the commit this deployment was built from. Nothing here is a mock-up:
          the diagrams describe the code that produced the proof on the previous page.
        </p>
      </header>

      <div className="tour-body">
        <nav className="tour-index" aria-label="Tour sections">
          <span className="label">Contents</span>
          <ol>
            {SECTIONS.map((section) => (
              <li key={section.id}>
                <a href={'#' + section.id}>
                  <span className="mono">{section.index}</span>
                  {section.title}
                </a>
              </li>
            ))}
          </ol>
        </nav>

        <div className="tour-sections">
          <section id="architecture" className="tour-section">
            <SectionHead label="01" title="Architecture" meta="one deployable unit" />
            <p className="prose">
              The client is a static React bundle served by the same ASP.NET Core process that owns the
              API. There is exactly one database. The release pipeline builds the bundle, embeds it in
              the API image, and ships that image by digest to Fly.io.
            </p>
            <ArchitectureDiagram />
            <p className="prose">
              The API is organised by feature rather than by technical layer: <code>Arena</code> owns the
              ledger and concurrency experiments, <code>Proof</code> assembles and verifies release
              evidence, <code>Health</code> owns liveness and migration start-up. Composition lives in{' '}
              <SourceLink source={SOURCES.program} />.
            </p>
          </section>

          <section id="lifecycle" className="tour-section">
            <SectionHead label="02" title="Request lifecycle" meta="POST /api/arena/transfers" />
            <ol className="lifecycle">
              <LifecycleStep
                index="1"
                title="Identity"
                body="A protected, HttpOnly cookie resolves an anonymous visitor id. No registration, no personal data."
                source={<SourceLink source={SOURCES.sandbox} />}
              />
              <LifecycleStep
                index="2"
                title="Route and limits"
                body="Rate limiting partitions on the visitor; the endpoint validates that both accounts belong to the caller's sandbox."
                source={<SourceLink source={SOURCES.endpoints} />}
              />
              <LifecycleStep
                index="3"
                title="Transaction"
                body="A scoped DbContext opens a READ COMMITTED transaction and applies the chosen concurrency strategy."
                source={<SourceLink source={SOURCES.executor} />}
              />
              <LifecycleStep
                index="4"
                title="Ledger write"
                body="Balance update, transfer row, one debit and one credit are committed together. The deferred trigger checks the pair at commit."
                source={<SourceLink source={SOURCES.migration} />}
              />
              <LifecycleStep
                index="5"
                title="Response"
                body="The API returns the outcome, attempt count, observed conflicts and current balances — measured values, not estimates."
                source={<SourceLink source={SOURCES.executor} />}
              />
            </ol>
          </section>

          <section id="schema" className="tour-section">
            <SectionHead label="03" title="Database schema" meta="schema: arena" />
            <p className="prose">
              Four tables. The accounts row is the contested resource; the ledger entries are the
              append-only record of what happened. Constraints are created with the schema in{' '}
              <SourceLink source={SOURCES.migration} />, not added later by hand.
            </p>
            <SchemaDiagram />
          </section>

          <section id="transactions" className="tour-section">
            <SectionHead label="04" title="Transaction processing" meta="two strategies, one invariant" />
            <div className="strategy-grid">
              <div className="strategy">
                <span className="label">Strategy A</span>
                <h3 className="display-m">Optimistic</h3>
                <ul className="tour-list">
                  <li>Read both accounts; the row carries PostgreSQL's xmin as a uint concurrency token.</li>
                  <li>Write the new balances and let the UPDATE predicate check xmin.</li>
                  <li>A stale writer gets DbUpdateConcurrencyException and retries with bounded exponential backoff plus jitter.</li>
                  <li>Retries and conflicts are persisted on the transfer and shown in the arena.</li>
                </ul>
                <SourceLink source={SOURCES.executor} />
              </div>
              <div className="strategy">
                <span className="label">Strategy B</span>
                <h3 className="display-m">Pessimistic</h3>
                <ul className="tour-list">
                  <li>Begin a transaction and SELECT … FOR UPDATE the sandbox accounts.</li>
                  <li>Lock order is the account primary key, independent of transfer direction.</li>
                  <li>Writers queue on the row lock, so xmin never conflicts and latency becomes lock wait time.</li>
                  <li>Locking every account in one order removes the classic deadlock window between A→B and B→A.</li>
                </ul>
                <SourceLink source={SOURCES.executor} />
              </div>
            </div>
            <p className="prose">
              Both strategies share the same invariant: a completed transfer has exactly two entries whose
              signed sum is zero, and no account may go negative. The database rejects a violation at
              commit, and the integration suite proves it by inserting a single-entry transfer.
            </p>
          </section>

          <section id="idempotency" className="tour-section">
            <SectionHead label="05" title="Idempotency" meta="unique (sandbox, key)" />
            <p className="prose">
              Every run generates a key per request. A unique index on <code>(sandbox_id, idempotency_key)</code>{' '}
              makes a duplicate physically impossible, and the executor treats both a pre-existing row and a
              unique violation as a replay: the original transfer id and outcome are returned, no second
              debit is written.
            </p>
            <p className="prose">
              The arena's replay mode sends fifty requests with five keys. The result is five completed
              transfers and forty-five replays, visible in the receipt log. The same-key race is covered by
              an integration test that fires eight concurrent requests with one key.
            </p>
            <SourceLink source={SOURCES.tests} />
          </section>

          <section id="ci" className="tour-section">
            <SectionHead label="06" title="CI and evidence" meta="one workflow run" />
            <p className="prose">
              Quality and release are one workflow run, so evidence can never cross a run boundary. The
              quality job restores, verifies formatting, builds in Release, runs unit and PostgreSQL
              integration tests, collects TRX and Cobertura output, enforces a 75% line-coverage gate and
              audits NuGet advisories. Only then may the image job run.
            </p>
            <CiPipeline />
            <p className="prose">
              The generator (<SourceLink source={SOURCES.generator} />) parses the real reports. A missing
              TRX, an absent coverage figure, a failed audit and a missing attestation are all recorded as
              missing and fail the release policy — none of them can silently become a pass. The manifest is
              written before the policy exit code so it can be uploaded even on failure.
            </p>
            <SourceLink source={SOURCES.workflow} />
          </section>

          <section id="deployment" className="tour-section">
            <SectionHead label="07" title="Deployment and attestation" meta="digest, not tag" />
            <p className="prose">
              The image is pushed to GitHub Container Registry and referenced by digest. GitHub attests
              build provenance and a custom quality predicate. Deployment passes that digest to Fly, and the
              application compares four values at request time:
            </p>
            <DeploymentDiagram />
            <p className="prose">
              The built digest comes from the release artifact, the attested digest from the attestation
              subject, the requested digest from the deploy job's intent artifact, and the observed digest
              from Fly's Machines API (<SourceLink source={SOURCES.flyProbe} />). The attestation is
              re-queried from GitHub on every request (<SourceLink source={SOURCES.attestationProbe} />);
              the manifest's own claim is not trusted on its own. The verdict logic lives in{' '}
              <SourceLink source={SOURCES.verifier} />, and the assembly step in{' '}
              <SourceLink source={SOURCES.proofService} />.
            </p>
            <p className="prose">
              A deployment environment variable is deliberately not used as evidence of the running image:
              it can be set to whatever the operator wants. If Fly cannot be reached, the release is
              UNKNOWN, not green.
            </p>
          </section>

          <section id="adr" className="tour-section">
            <SectionHead label="08" title="Decision records" meta="docs/adr" />
            <div className="adr-grid">
              {ADRS.map((adr) => (
                <article key={adr.id} className="adr">
                  <span className="label">ADR {adr.id}</span>
                  <h3>{adr.title}</h3>
                  <p>
                    <strong>Decision.</strong> {adr.decision}
                  </p>
                  <p>
                    <strong>Why.</strong> {adr.why}
                  </p>
                </article>
              ))}
            </div>
            <p className="prose">
              Full records live in <code>docs/adr/</code> in the repository, next to the code they describe.
            </p>
          </section>

          <section id="history" className="tour-section">
            <SectionHead label="09" title="Commit history" meta="real work, in order" />
            <p className="prose">
              The repository history records the work as it happened: the evidence engine first, then the
              API, then the tests, the client, the pipeline and the documentation. It is not a fabricated
              timeline, and no commit claims work that was not verified at the time.
            </p>
            {meta?.repositoryUrl ? (
              <a className="btn" href={meta.repositoryUrl + '/commits'} target="_blank" rel="noreferrer">
                Open the history <ArrowUpRightIcon size={12} />
              </a>
            ) : null}
          </section>
        </div>
      </div>
    </article>
  )
}

function LifecycleStep({
  index,
  title,
  body,
  source,
}: {
  index: string
  title: string
  body: string
  source: ReactNode
}) {
  return (
    <li className="lifecycle-step">
      <span className="lifecycle-index mono">{index}</span>
      <div>
        <h3>{title}</h3>
        <p>{body}</p>
        <div className="lifecycle-source">{source}</div>
      </div>
    </li>
  )
}

function ArchitectureDiagram() {
  return (
    <figure className="diagram diagram-architecture">
      <svg viewBox="0 0 960 330" role="img" aria-label="Browser, API, PostgreSQL, GitHub Actions, registry and Fly.io">
        <defs>
          <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" markerHeight="6" orient="auto">
            <path d="M 0 0 L 10 5 L 0 10 z" fill="var(--line-strong)" />
          </marker>
        </defs>

        <g className="diagram-node">
          <rect x="20" y="110" width="190" height="86" rx="2" />
          <text x="38" y="140" className="diagram-title">Browser</text>
          <text x="38" y="162" className="diagram-body">React · TypeScript</text>
          <text x="38" y="180" className="diagram-body">proof / arena / tour</text>
        </g>

        <line x1="210" y1="153" x2="330" y2="153" className="diagram-link" markerEnd="url(#arrow)" />
        <text x="236" y="143" className="diagram-label">HTTPS</text>

        <g className="diagram-node">
          <rect x="330" y="70" width="250" height="166" rx="2" />
          <text x="348" y="100" className="diagram-title">ASP.NET Core 10</text>
          <text x="348" y="124" className="diagram-body">Arena · Proof · Health</text>
          <text x="348" y="146" className="diagram-body">rate limits · cookie identity</text>
          <text x="348" y="168" className="diagram-body">EF Core · Npgsql</text>
          <text x="348" y="196" className="diagram-body">no visitor code execution</text>
          <text x="348" y="218" className="diagram-body">no per-visitor containers</text>
        </g>

        <line x1="580" y1="153" x2="700" y2="153" className="diagram-link" markerEnd="url(#arrow)" />
        <text x="606" y="143" className="diagram-label">SQL</text>

        <g className="diagram-node">
          <rect x="700" y="110" width="230" height="86" rx="2" />
          <text x="718" y="140" className="diagram-title">PostgreSQL 17</text>
          <text x="718" y="162" className="diagram-body">double-entry ledger</text>
          <text x="718" y="180" className="diagram-body">xmin · row locks · triggers</text>
        </g>

        <g className="diagram-supply">
          <text x="20" y="262" className="diagram-label">RELEASE PATH</text>
          <text x="20" y="288" className="diagram-body">GitHub Actions → GHCR (digest) → Fly.io Machines</text>
          <line x1="330" y1="284" x2="560" y2="284" className="diagram-dashed" />
          <text x="580" y="288" className="diagram-body">attestations queried live</text>
        </g>
      </svg>
      <figcaption>One process, one database, one image. The release path supplies the digests the API verifies.</figcaption>
    </figure>
  )
}

function SchemaDiagram() {
  return (
    <div className="schema">
      <div className="schema-table">
        <div className="schema-head">
          <span>sandboxes</span>
          <span className="mono">row</span>
        </div>
        <ul>
          <li><code>id</code> uuid PK</li>
          <li><code>visitor_id</code> uuid · unique when active</li>
          <li><code>scenario_key</code> text</li>
          <li><code>initial_balance_cents</code> bigint</li>
          <li><code>archived_at</code> timestamptz</li>
        </ul>
      </div>
      <div className="schema-table">
        <div className="schema-head">
          <span>accounts</span>
          <span className="mono">row · contested</span>
        </div>
        <ul>
          <li><code>id</code> uuid PK</li>
          <li><code>sandbox_id</code> → sandboxes</li>
          <li><code>code</code> · unique per sandbox</li>
          <li><code>balance_cents</code> bigint · CHECK ≥ 0</li>
          <li><code>xmin</code> xid · concurrency token</li>
        </ul>
      </div>
      <div className="schema-table">
        <div className="schema-head">
          <span>transfers</span>
          <span className="mono">intent</span>
        </div>
        <ul>
          <li><code>id</code> uuid PK</li>
          <li><code>sandbox_id</code> · <code>amount_cents</code></li>
          <li><code>strategy</code> · <code>status</code></li>
          <li><code>idempotency_key</code> · unique with sandbox</li>
          <li><code>attempts</code> · <code>conflicts</code></li>
        </ul>
      </div>
      <div className="schema-table">
        <div className="schema-head">
          <span>ledger_entries</span>
          <span className="mono">append-only</span>
        </div>
        <ul>
          <li><code>id</code> uuid PK</li>
          <li><code>transfer_id</code> → transfers</li>
          <li><code>account_id</code> → accounts</li>
          <li><code>direction</code> debit | credit</li>
          <li>unique <code>(transfer_id, direction)</code></li>
        </ul>
      </div>
      <p className="schema-note">
        Deferred constraint trigger <code>trg_ledger_entries_balanced</code>: two entries per completed
        transfer, signed sum zero, checked at COMMIT. <code>trg_ledger_entries_append_only</code> rejects
        UPDATE and DELETE.
      </p>
    </div>
  )
}

function CiPipeline() {
  const stages = [
    { step: 'restore', detail: 'locked dependencies' },
    { step: 'format', detail: 'dotnet format --verify-no-changes' },
    { step: 'build', detail: 'Release' },
    { step: 'test', detail: 'unit + PostgreSQL' },
    { step: 'coverage', detail: '≥ 75% line' },
    { step: 'audit', detail: 'NuGet advisories' },
    { step: 'evidence', detail: 'ci-evidence.json' },
    { step: 'image', detail: 'buildx · digest' },
    { step: 'attest', detail: 'provenance + quality' },
    { step: 'deploy', detail: 'fly deploy @digest' },
  ]

  return (
    <div className="pipeline" role="img" aria-label="CI pipeline stages from restore to deploy">
      {stages.map((stage, index) => (
        <div className="pipeline-stage" key={stage.step}>
          <span className="pipeline-index mono">{String(index + 1).padStart(2, '0')}</span>
          <span className="pipeline-name">{stage.step}</span>
          <span className="pipeline-detail">{stage.detail}</span>
        </div>
      ))}
    </div>
  )
}

function DeploymentDiagram() {
  const nodes = [
    { title: 'Built', detail: 'image job output' },
    { title: 'Attested', detail: 'gh attestation subject' },
    { title: 'Requested', detail: 'deploy intent artifact' },
    { title: 'Observed', detail: 'Fly Machines API' },
  ]

  return (
    <figure className="diagram diagram-deploy">
      <svg viewBox="0 0 960 150" role="img" aria-label="Four digest sources compared by the verifier">
        {nodes.map((node, index) => {
          const x = 20 + index * 235
          return (
            <g key={node.title} className="diagram-node">
              <rect x={x} y="34" width="200" height="72" rx="2" />
              <text x={x + 16} y="60" className="diagram-title">{node.title}</text>
              <text x={x + 16} y="84" className="diagram-body">{node.detail}</text>
              {index < nodes.length - 1 ? (
                <line x1={x + 200} y1="70" x2={x + 235} y2="70" className="diagram-link" />
              ) : null}
            </g>
          )
        })}
        <text x="20" y="132" className="diagram-label">
          all four equal → verified · any missing → unknown · any disagreement → failed
        </text>
      </svg>
      <figcaption>The verifier compares every adjacent pair, not just the first and last.</figcaption>
    </figure>
  )
}