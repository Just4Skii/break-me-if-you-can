import { useApi } from '../hooks/useApi'
import type { Meta, ProofCheck, ProofReport } from '../types'
import {
  formatAge,
  formatNumber,
  formatTimestamp,
  shortSha,
  stateWord,
} from '../format'
import { ArrowUpRightIcon, RefreshIcon } from '../components/Icons'
import { CopyButton } from '../components/CopyButton'
import { CoverageDial } from '../components/CoverageDial'
import { DigestChain } from '../components/DigestChain'
import { SectionHead } from '../components/SectionHead'
import { StateMark } from '../components/StateMark'

export function ProofPage() {
  const proof = useApi<ProofReport>('/api/proof')
  const meta = useApi<Meta>('/api/meta')

  if (proof.status === 'loading') {
    return <ProofSkeleton />
  }

  if (proof.status === 'error') {
    return (
      <section className="proof-error">
        <span className="label">Engineering proof</span>
        <h1 className="display-l">The proof endpoint did not answer.</h1>
        <p className="prose">{proof.error}</p>
        <p className="prose">
          Nothing is shown rather than a placeholder success state. The endpoint is the only source of this
          page.
        </p>
        <button type="button" className="btn" onClick={proof.reload}>
          <RefreshIcon size={14} /> Try again
        </button>
      </section>
    )
  }

  const report = proof.data
  const repositoryUrl = meta.status === 'success' ? meta.data.repositoryUrl : undefined

  return (
    <article className="proof">
      <div className="proof-masthead">
        <span className="label">Engineering proof · GET /proof</span>
        <span className="mono proof-masthead-right">
          assembled {formatTimestamp(report.generatedAt)} · evidence {report.evidenceSource}
        </span>
      </div>

      <Verdict report={report} repositoryUrl={repositoryUrl} onRefresh={proof.reload} />

      <ChecksSection checks={report.checks} />

      <Readouts report={report} />

      <ArtifactSection report={report} />

      <SecuritySection report={report} />
    </article>
  )
}

function Verdict({
  report,
  repositoryUrl,
  onRefresh,
}: {
  report: ProofReport
  repositoryUrl?: string
  onRefresh: () => void
}) {
  const commitUrl =
    repositoryUrl && report.source.commitSha
      ? repositoryUrl + '/commit/' + report.source.commitSha
      : undefined

  return (
    <header className={'verdict verdict-' + report.state}>
      <div className="verdict-main">
        <div className="verdict-eyebrow">
          <span className={'verdict-badge state-' + report.state}>
            <span className="dot" />
            {stateWord(report.state)}
          </span>
          <span className="verdict-commit mono">
            {commitUrl ? (
              <a className="link" href={commitUrl} target="_blank" rel="noreferrer">
                {shortSha(report.source.commitSha)}
              </a>
            ) : (
              shortSha(report.source.commitSha)
            )}
            {report.source.branch ? ' · ' + report.source.branch : ''}
          </span>
        </div>

        <h1 className="display-xl verdict-word">
          {stateWord(report.state)}
          <span className={'verdict-period state-' + report.state}>.</span>
        </h1>

        <p className="verdict-summary">{report.summary}</p>

        <div className="verdict-meta">
          <div className="verdict-meta-item">
            <span className="label">Workflow</span>
            {report.workflow.runUrl ? (
              <a className="link-arrow" href={report.workflow.runUrl} target="_blank" rel="noreferrer">
                {report.workflow.name || 'workflow'} #{report.workflow.runId || '—'}
                <ArrowUpRightIcon size={11} />
              </a>
            ) : (
              <span className="mono">not recorded</span>
            )}
          </div>
          <div className="verdict-meta-item">
            <span className="label">Evidence age</span>
            <span className={'mono ' + (report.freshness.stale ? 'state-failed' : '')}>
              {formatAge(report.freshness.ageSeconds)}
            </span>
          </div>
          <div className="verdict-meta-item">
            <span className="label">Deployed</span>
            <span className={'mono state-' + report.deployment.state}>{report.deployment.health}</span>
          </div>
          <button type="button" className="btn btn-ghost verdict-refresh" onClick={onRefresh}>
            <RefreshIcon size={13} /> Refresh
          </button>
        </div>

        {report.notes.length > 0 ? (
          <div className="verdict-notes">
            <span className="label">Why it is not verified</span>
            <ul>
              {report.notes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
          </div>
        ) : null}
      </div>

      <DigestChain report={report} />
    </header>
  )
}

function ChecksSection({ checks }: { checks: ProofCheck[] }) {
  const required = checks.filter((check) => check.required)
  const verified = required.filter((check) => check.state === 'verified')
  const flagged = checks.filter((check) => check.state !== 'verified')
  const clean = checks.filter((check) => check.state === 'verified')

  return (
    <section className="checks" aria-labelledby="checks-title">
      <SectionHead
        label="Evidence ledger"
        title={<span id="checks-title">Required checks</span>}
        meta={verified.length + ' / ' + required.length + ' verified'}
      />
      <table className="checks-table">
        <thead>
          <tr>
            <th scope="col" className="col-state">
              State
            </th>
            <th scope="col" className="col-check">
              Check
            </th>
            <th scope="col" className="col-detail">
              Observed
            </th>
          </tr>
        </thead>
        <tbody>
          {flagged.length > 0 ? (
            <tr className="check-group">
              <td colSpan={3}>
                Not verified — {flagged.length}
                <span className="check-group-note">the reason each release is not verified</span>
              </td>
            </tr>
          ) : null}
          {flagged.map(renderCheckRow)}
          {clean.length > 0 ? (
            <tr className="check-group">
              <td colSpan={3}>Verified — {clean.length}</td>
            </tr>
          ) : null}
          {clean.map(renderCheckRow)}
        </tbody>
      </table>
    </section>
  )
}

function renderCheckRow(check: ProofCheck) {
  return (
    <tr key={check.id} className={'check-row check-' + check.state}>
      <td data-label="State">
        <span className="check-state">
          <StateMark state={check.state} />
          <span className={'mono check-state-word state-' + check.state}>{check.state}</span>
        </span>
      </td>
      <td data-label="Check">
        <span className="check-label">{check.label}</span>
        <code className="check-id">{check.id}</code>
      </td>
      <td data-label="Observed">
        <p className="check-detail">{check.detail}</p>
      </td>
    </tr>
  )
}

function Readouts({ report }: { report: ProofReport }) {
  const severities = ['critical', 'high', 'moderate', 'low'] as const
  const totalAdvisories = report.dependencyAudit.directCount + report.dependencyAudit.transitiveCount

  return (
    <section className="readouts" aria-label="Test, coverage and dependency evidence">
      <div className="readout">
        <SectionHead label="Automated tests" title="Outcomes" meta={report.tests.state} />
        <div className="readout-big">
          <span className="readout-number state-verified">{formatNumber(report.tests.passed)}</span>
          <span className="readout-of">/ {formatNumber(report.tests.total)} passed</span>
        </div>
        <div className="test-split">
          <span className="state-failed">{report.tests.failed} failed</span>
          <span className="state-unknown">{report.tests.skipped} skipped</span>
          <span className="mono">{report.tests.durationSeconds.toFixed(1)}s</span>
        </div>
        <ul className="assembly-list">
          {report.tests.assemblies.map((assembly) => {
            const ratio = assembly.total > 0 ? assembly.passed / assembly.total : 0
            return (
              <li key={assembly.assembly} className="assembly">
                <div className="assembly-head">
                  <code>{assembly.assembly}</code>
                  <span className="mono">{assembly.total}</span>
                </div>
                <div className="assembly-bar" aria-hidden="true">
                  <span className="assembly-fill" style={{ width: ratio * 100 + '%' }} />
                </div>
              </li>
            )
          })}
          {report.tests.assemblies.length === 0 ? <li className="readout-empty">No TRX report recorded.</li> : null}
        </ul>
      </div>

      <div className="readout">
        <SectionHead
          label="Coverage"
          title="Line and branch"
          meta={report.coverage.meetsThreshold === true ? 'gate passed' : report.coverage.state}
        />
        <CoverageDial
          linePercent={report.coverage.linePercent}
          branchPercent={report.coverage.branchPercent}
          threshold={report.coverage.thresholdPercent}
          state={report.coverage.state}
        />
        <dl className="kv kv-compact">
          <div>
            <dt>covered</dt>
            <dd className="mono">
              {report.coverage.linesCovered !== undefined
                ? formatNumber(report.coverage.linesCovered) + ' / ' + formatNumber(report.coverage.linesValid ?? 0)
                : '—'}
            </dd>
          </div>
          <div>
            <dt>collector</dt>
            <dd className="mono">coverlet</dd>
          </div>
          <div>
            <dt>report</dt>
            <dd className="mono truncate">{report.coverage.reportArtifact ?? 'not recorded'}</dd>
          </div>
        </dl>
      </div>

      <div className="readout">
        <SectionHead
          label="Dependencies"
          title="NuGet audit"
          meta={report.dependencyAudit.status}
        />
        <div className="readout-big">
          <span
            className={'readout-number ' + (totalAdvisories === 0 ? 'state-verified' : 'state-failed')}
          >
            {totalAdvisories}
          </span>
          <span className="readout-of">known advisories</span>
        </div>
        <ul className="severity-list">
          {severities.map((severity) => {
            const count = report.dependencyAudit.bySeverity[severity] ?? 0
            return (
              <li key={severity} className={'severity' + (count > 0 ? ' severity-hit' : '')}>
                <span className="severity-name">{severity}</span>
                <span className="severity-track">
                  <span className="severity-fill" style={{ width: count > 0 ? Math.min(100, 18 + count * 22) + '%' : '0%' }} />
                </span>
                <span className="mono severity-count">{count}</span>
              </li>
            )
          })}
        </ul>
        <p className="readout-note">{report.dependencyAudit.scope}</p>
      </div>
    </section>
  )
}

function ArtifactSection({ report }: { report: ProofReport }) {
  const digests = [
    { label: 'Built', value: report.image.builtDigest },
    { label: 'Attested', value: report.image.attestedDigest },
    { label: 'Requested', value: report.image.requestedDeploymentDigest },
    { label: 'Observed', value: report.image.observedRunningDigest },
  ]

  return (
    <section className="artifact" aria-label="Container image and attestation">
      <div className="artifact-left">
        <SectionHead
          label="Container image"
          title="Release artifact"
          meta={report.checks.find((check) => check.id === 'digest.chain')?.state ?? 'unknown'}
        />

        <div className="image-ref">
          <span className="label">reference</span>
          <code>{report.image.reference || 'not recorded'}</code>
        </div>

        <table className="digest-table">
          <tbody>
            {digests.map((entry) => (
              <tr key={entry.label}>
                <th scope="row">{entry.label}</th>
                <td>
                  <code>{entry.value ?? 'not observed'}</code>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="verify-block">
          <div className="verify-head">
            <span className="label">Verify it yourself</span>
            {report.verifyCommand ? <CopyButton value={report.verifyCommand} label="Copy command" /> : null}
          </div>
          {report.verifyCommand ? (
            <pre className="code-block">
              <code>{report.verifyCommand}</code>
            </pre>
          ) : (
            <p className="readout-note">
              No attestation command can be produced until an attested digest is recorded for this build.
            </p>
          )}
          <p className="readout-note">
            Requires the GitHub CLI. The command checks the attestation against this repository and the exact
            image digest; it does not trust this page.
          </p>
        </div>
      </div>

      <div className="artifact-right">
        <SectionHead label="Provenance" title="Attestation" meta={report.attestation.state} />

        <dl className="kv">
          <div>
            <dt>predicate</dt>
            <dd className="mono truncate">{report.attestation.predicateType ?? 'not recorded'}</dd>
          </div>
          <div>
            <dt>subject</dt>
            <dd className="mono truncate">{report.attestation.subjectDigest ?? 'not recorded'}</dd>
          </div>
          <div>
            <dt>repository</dt>
            <dd className="mono">{report.attestation.repository ?? 'not recorded'}</dd>
          </div>
          <div>
            <dt>commit</dt>
            <dd className="mono">{shortSha(report.attestation.commitSha)}</dd>
          </div>
          <div>
            <dt>workflow</dt>
            <dd className="mono truncate">{report.attestation.workflowRef ?? 'not recorded'}</dd>
          </div>
          <div>
            <dt>queried</dt>
            <dd className="mono">
              {report.attestation.sourceUrl ? (
                <a className="link-arrow" href={report.attestation.sourceUrl} target="_blank" rel="noreferrer">
                  {report.attestation.source} <ArrowUpRightIcon size={11} />
                </a>
              ) : (
                report.attestation.source
              )}
            </dd>
          </div>
        </dl>

        <p className="readout-note">{report.attestation.note}</p>

        <div className="deployment-card">
          <div className="deployment-head">
            <span className="label">Deployment</span>
            <span className={'mono state-' + report.deployment.state}>{report.deployment.health}</span>
          </div>
          <dl className="kv kv-compact">
            <div>
              <dt>app</dt>
              <dd className="mono">{report.deployment.appName ?? 'not recorded'}</dd>
            </div>
            <div>
              <dt>machines</dt>
              <dd className="mono truncate">
                {report.deployment.machineCount && report.deployment.machineCount > 1
                  ? report.deployment.machineCount + ' running, all must agree'
                  : report.deployment.machineId ?? 'not recorded'}
              </dd>
            </div>
            <div>
              <dt>region</dt>
              <dd className="mono">{report.deployment.region ?? 'unknown'}</dd>
            </div>
            <div>
              <dt>observed</dt>
              <dd className="mono">{formatTimestamp(report.deployment.observedAt)}</dd>
            </div>
            <div>
              <dt>source</dt>
              <dd className="mono">
                {report.deployment.sourceUrl ? (
                  <a className="link-arrow" href={report.deployment.sourceUrl} target="_blank" rel="noreferrer">
                    {report.deployment.source} <ArrowUpRightIcon size={11} />
                  </a>
                ) : (
                  report.deployment.source
                )}
              </dd>
            </div>
          </dl>
          {report.deployment.detail ? <p className="readout-note">{report.deployment.detail}</p> : null}
        </div>
      </div>
    </section>
  )
}

function SecuritySection({ report }: { report: ProofReport }) {
  return (
    <section className="security" aria-label="Security posture">
      <SectionHead label="Security posture" title="What this proves, and what it does not" />
      <div className="security-grid">
        <div>
          <p className="security-disclaimer">{report.securityPosture.disclaimer}</p>
          <p className="readout-note">{report.securityPosture.scanScope}</p>
        </div>
        <div className="security-lists">
          <div>
            <span className="label">Controls in this release</span>
            <ul className="checklist">
              {report.securityPosture.implementedControls.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </div>
          <div>
            <span className="label">Not assessed</span>
            <ul className="checklist checklist-muted">
              {report.securityPosture.notAssessed.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </section>
  )
}

function ProofSkeleton() {
  return (
    <div className="proof proof-skeleton" aria-busy="true" aria-label="Loading evidence">
      <div className="proof-masthead">
        <span className="label">Engineering proof · GET /proof</span>
      </div>
      <div className="skeleton skeleton-verdict" />
      <div className="skeleton skeleton-checks" />
      <div className="skeleton skeleton-readouts" />
    </div>
  )
}
