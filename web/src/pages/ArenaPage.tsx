import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, postJson } from '../api'
import { useApi } from '../hooks/useApi'
import type {
  ArenaRunResult,
  ChallengeRunView,
  ChallengeView,
  LedgerView,
  RunRequest,
  SandboxView,
} from '../types'
import { formatDurationMs, formatNumber, formatTimestamp, formatZar, shortSha } from '../format'
import { CheckIcon, CrossIcon, PlayIcon, RefreshIcon } from '../components/Icons'
import { setSandboxToken } from '../session'
import { getStoredPlayer } from '../auth'
import { SectionHead } from '../components/SectionHead'

type Strategy = 'optimistic' | 'pessimistic'

interface FormState {
  strategy: Strategy
  requests: number
  concurrency: number
  amountRands: number
  pattern: 'alternating' | 'a_to_b'
  idempotency: 'unique' | 'replay'
}

const DEFAULT_FORM: FormState = {
  strategy: 'optimistic',
  requests: 50,
  concurrency: 8,
  amountRands: 10,
  pattern: 'alternating',
  idempotency: 'unique',
}

export function ArenaPage() {
  const session = useApi<SandboxView>('/api/arena/session')
  const [refresh, setRefresh] = useState(0)
  const ledger = useApi<LedgerView>('/api/arena/ledger?limit=12', refresh)
  const [form, setForm] = useState<FormState>(DEFAULT_FORM)
  const [running, setRunning] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [result, setResult] = useState<ArenaRunResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [history, setHistory] = useState<Partial<Record<Strategy, ArenaRunResult>>>({})
  const [showAllReceipts, setShowAllReceipts] = useState(false)

  const player = getStoredPlayer()
  const challenges = useApi<ChallengeView[]>('/api/challenges')
  const [rankedKey, setRankedKey] = useState('optimistic-8')
  const [ranked, setRanked] = useState<ChallengeRunView | null>(null)
  const [rankedRunning, setRankedRunning] = useState(false)
  const [rankedError, setRankedError] = useState<string | null>(null)

  const limits = session.status === 'success' ? session.data.limits : undefined
  const sessionToken = session.status === 'success' ? session.data.sandboxToken : undefined

  useEffect(() => {
    if (sessionToken) {
      setSandboxToken(sessionToken)
    }
  }, [sessionToken])

  const update = (patch: Partial<FormState>) => setForm((current) => ({ ...current, ...patch }))

  const selectedChallenge = challenges.status === 'success'
    ? challenges.data.find((item) => item.key === rankedKey)
    : undefined

  const runRanked = async () => {
    if (!player) return

    setRankedRunning(true)
    setRankedError(null)

    try {
      const data = await postJson<ChallengeRunView>(
        '/api/challenges/' + encodeURIComponent(rankedKey) + '/run',
        {},
      )
      setRanked(data)
      setRefresh((value) => value + 1)
    } catch (caught) {
      setRankedError(caught instanceof ApiError ? caught.message : 'The ranked run could not be completed.')
    } finally {
      setRankedRunning(false)
    }
  }

  const run = async () => {
    setRunning(true)
    setError(null)

    try {
      const body: RunRequest = {
        strategy: form.strategy,
        requests: form.requests,
        concurrency: form.concurrency,
        amountCents: Math.round(form.amountRands * 100),
        pattern: form.pattern,
        idempotency: form.idempotency,
        replayKeys: 5,
      }

      const data = await postJson<ArenaRunResult>('/api/arena/runs', body)
      setResult(data)
      setHistory((current) => ({ ...current, [data.strategy]: data }))
      setRefresh((value) => value + 1)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The run could not be completed.')
    } finally {
      setRunning(false)
    }
  }

  const reset = async () => {
    setResetting(true)
    setError(null)

    try {
      const resetSession = await postJson<SandboxView>('/api/arena/reset', {})
      setSandboxToken(resetSession.sandboxToken)
      session.reload()
      setResult(null)
      setHistory({})
      setRefresh((value) => value + 1)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The sandbox could not be reset.')
    } finally {
      setResetting(false)
    }
  }

  const announce = result
    ? 'Run complete. ' +
      result.accepted +
      ' accepted, ' +
      result.rejected +
      ' rejected, ' +
      result.conflicts +
      ' conflicts, ' +
      result.totalRetries +
      ' retries.'
    : ''

  return (
    <article className="arena">
      <header className="arena-head">
        <span className="label">Race condition arena · double-entry ledger</span>
        <h1 className="display-l">Put the ledger under load.</h1>
        <p className="arena-intro">
          Two accounts, one PostgreSQL database and a burst of concurrent transfers. Choose how the
          accounts are protected, run the burst, and read the measured result: accepted requests,
          rejected requests, retries, conflicts, latency and final balances.
        </p>
        <div className="arena-chips">
          {session.status === 'success' ? (
            <>
              <span className="chip">
                sandbox <code>{shortSha(session.data.sandboxId)}</code>
              </span>
              <span className="chip">
                {session.data.scenarioName} · {formatZar(session.data.initialBalanceCents)} per account
              </span>
              <span className="chip">
                global cap {session.data.limits.globalConcurrencyLimit} transactions
              </span>
            </>
          ) : (
            <span className="chip">{session.status === 'error' ? 'arena unavailable' : 'provisioning sandbox'}</span>
          )}
        </div>
      </header>

      <section className="ranked-panel" aria-label="Ranked challenge">
        <div className="ranked-head">
          <div>
            <span className="label">Ranked challenge · server-executed</span>
            <h2>Put a verified score on the board.</h2>
          </div>
          <Link className="link-arrow" to="/leaderboard">Leaderboard →</Link>
        </div>

        {player ? (
          <>
            <div className="ranked-controls">
              <select
                className="ranked-select"
                aria-label="Ranked challenge"
                value={rankedKey}
                onChange={(event) => { setRankedKey(event.target.value); setRanked(null) }}
              >
                {(challenges.status === 'success' ? challenges.data : []).map((item) => (
                  <option key={item.key} value={item.key}>{item.name}</option>
                ))}
              </select>
              <button className="btn btn-primary run-button ranked-run-button" type="button" onClick={runRanked} disabled={rankedRunning}>
                {rankedRunning ? 'Running…' : 'Run ranked challenge'}
              </button>
            </div>
            <p className="control-note">
              {selectedChallenge?.summary ?? 'The server fixes the transfers, concurrency and strategy; the client cannot submit a score.'}
            </p>
            {rankedError ? <p className="arena-error" role="alert">{rankedError}</p> : null}
            {ranked ? (
              <div className="ranked-result">
                <div className="ranked-verdict">
                  <span className={'run-state ' + (ranked.verified ? 'ok' : 'bad')}>
                    {ranked.verified ? 'Verified' : 'Not ranked'}
                  </span>
                  <span className="mono">{ranked.challenge.name}</span>
                </div>
                <div className="ranked-stats">
                  <div><span className="label">Score</span><strong>{formatNumber(ranked.score)}</strong></div>
                  <div><span className="label">Rank</span><strong>{ranked.rank ? '#' + ranked.rank : '—'}</strong></div>
                  <div><span className="label">Personal best</span><strong>{formatNumber(ranked.personalBest)}</strong></div>
                  <div><span className="label">Accepted</span><strong>{ranked.run.accepted} / {ranked.run.requested}</strong></div>
                </div>
                <Link className="link-arrow" to="/leaderboard">See where you stand →</Link>
              </div>
            ) : null}
          </>
        ) : (
          <p className="control-note">
            The ranked run is saved only after you sign in.{' '}
            <Link className="link-arrow" to="/login?next=/arena">Sign in or create an account →</Link>
          </p>
        )}
      </section>

      <div className="arena-body">
        <aside className="arena-controls" aria-label="Run configuration">
          <div className="controls-block">
            <span className="label">Concurrency control</span>
            <div className="segmented" role="radiogroup" aria-label="Concurrency strategy">
              {(['optimistic', 'pessimistic'] as const).map((strategy) => (
                <button
                  key={strategy}
                  type="button"
                  role="radio"
                  aria-checked={form.strategy === strategy}
                  className={'segment' + (form.strategy === strategy ? ' active' : '')}
                  onClick={() => update({ strategy })}
                >
                  {strategy}
                </button>
              ))}
            </div>
            <p className="control-note">
              {form.strategy === 'optimistic'
                ? 'Read, modify, write. PostgreSQL xmin rejects the stale writer and the transfer is retried with bounded backoff.'
                : 'SELECT … FOR UPDATE locks both accounts in id order before the balance changes. Writers queue instead of colliding.'}
            </p>
          </div>

          <div className="controls-block">
            <div className="control-row">
              <label className="label" htmlFor="requests">
                Requests
              </label>
              <output className="control-value mono" htmlFor="requests">
                {form.requests}
              </output>
            </div>
            <input
              id="requests"
              className="range"
              type="range"
              min={1}
              max={limits?.maxRequestsPerRun ?? 50}
              value={form.requests}
              onChange={(event) => update({ requests: Number(event.target.value) })}
            />
          </div>

          <div className="controls-block">
            <div className="control-row">
              <label className="label" htmlFor="concurrency">
                Concurrency
              </label>
              <output className="control-value mono" htmlFor="concurrency">
                {form.concurrency}
              </output>
            </div>
            <input
              id="concurrency"
              className="range"
              type="range"
              min={1}
              max={limits?.maxConcurrencyPerRun ?? 16}
              value={form.concurrency}
              onChange={(event) => update({ concurrency: Number(event.target.value) })}
            />
          </div>

          <div className="controls-block">
            <label className="label" htmlFor="amount">
              Amount per transfer (ZAR)
            </label>
            <input
              id="amount"
              className="input"
              type="number"
              min={0.5}
              step={0.5}
              max={(limits?.maxAmountCents ?? 10000) / 100}
              value={form.amountRands}
              onChange={(event) => update({ amountRands: Number(event.target.value) })}
            />
          </div>

          <div className="controls-block">
            <span className="label">Transfer pattern</span>
            <div className="segmented" role="radiogroup" aria-label="Transfer pattern">
              <button
                type="button"
                role="radio"
                aria-checked={form.pattern === 'alternating'}
                className={'segment' + (form.pattern === 'alternating' ? ' active' : '')}
                onClick={() => update({ pattern: 'alternating' })}
              >
                alternating
              </button>
              <button
                type="button"
                role="radio"
                aria-checked={form.pattern === 'a_to_b'}
                className={'segment' + (form.pattern === 'a_to_b' ? ' active' : '')}
                onClick={() => update({ pattern: 'a_to_b' })}
              >
                A → B
              </button>
            </div>
          </div>

          <div className="controls-block">
            <span className="label">Idempotency</span>
            <div className="segmented" role="radiogroup" aria-label="Idempotency mode">
              <button
                type="button"
                role="radio"
                aria-checked={form.idempotency === 'unique'}
                className={'segment' + (form.idempotency === 'unique' ? ' active' : '')}
                onClick={() => update({ idempotency: 'unique' })}
              >
                unique keys
              </button>
              <button
                type="button"
                role="radio"
                aria-checked={form.idempotency === 'replay'}
                className={'segment' + (form.idempotency === 'replay' ? ' active' : '')}
                onClick={() => update({ idempotency: 'replay' })}
              >
                replay × 5
              </button>
            </div>
          </div>

          <button type="button" className="btn btn-primary run-button" onClick={run} disabled={running || session.status !== 'success'}>
            <PlayIcon size={13} />
            {running ? 'Running burst…' : 'Run ' + form.requests + ' transfers'}
          </button>

          <button type="button" className="btn btn-ghost reset-button" onClick={reset} disabled={resetting || running}>
            <RefreshIcon size={13} />
            {resetting ? 'Resetting…' : 'Reset sandbox'}
          </button>

          {error ? (
            <p className="arena-error" role="alert">
              {error}
            </p>
          ) : null}

          <p className="control-footnote">
            Limits: {limits?.maxRequestsPerRun ?? '—'} requests, {limits?.maxConcurrencyPerRun ?? '—'} concurrent,
            {' '}at most {formatZar(limits?.maxAmountCents ?? 0)} each. Reset archives the sandbox; the ledger is
            never rewritten.
          </p>
        </aside>

        <section className="arena-results" aria-label="Run results">
          <div aria-live="polite" className="sr-only">
            {announce}
          </div>

          {result ? (
            <>
              <div className="result-head">
                <SectionHead
                  label={'Run · ' + result.strategy}
                  title={'Burst receipt'}
                  meta={formatDurationMs(result.durationMs) + ' wall'}
                />
                <div className="run-meta">
                  <span className="run-state">
                    <span className="dot" /> completed {formatTimestamp(result.completedAt)}
                  </span>
                  <span className="run-id mono">run {shortSha(result.runId)}</span>
                </div>
              </div>

              <div className="stat-row">
                <Stat label="accepted" value={formatNumber(result.accepted)} tone="verified" />
                <Stat label="rejected" value={formatNumber(result.rejected)} tone={result.rejected > 0 ? 'failed' : 'muted'} />
                <Stat label="replayed" value={formatNumber(result.replayed)} tone="muted" />
                <Stat label="retries" value={formatNumber(result.totalRetries)} tone="muted" />
                <Stat label="conflicts" value={formatNumber(result.conflicts)} tone={result.conflicts > 0 ? 'verified' : 'muted'} />
                <Stat label="wall" value={formatDurationMs(result.durationMs)} tone="muted" />
              </div>

              {history.optimistic && history.pessimistic ? (
                <div className="compare-block">
                  <SectionHead label="Comparison" title="Optimistic vs pessimistic" meta="same scenario" />
                  <table className="compare">
                    <thead>
                      <tr>
                        <th>Strategy</th>
                        <th>Accepted</th>
                        <th>Conflicts</th>
                        <th>Retries</th>
                        <th>p50</th>
                        <th>p95</th>
                        <th>Wall</th>
                      </tr>
                    </thead>
                    <tbody>
                      {[history.optimistic, history.pessimistic].map((run) => (
                        <tr key={run.strategy} className={run.strategy === result.strategy ? 'compare-active' : ''}>
                          <td className="mono">{run.strategy}</td>
                          <td className="mono">{run.accepted}</td>
                          <td className="mono">{run.conflicts}</td>
                          <td className="mono">{run.totalRetries}</td>
                          <td className="mono">{formatDurationMs(run.latency.p50Ms)}</td>
                          <td className="mono">{formatDurationMs(run.latency.p95Ms)}</td>
                          <td className="mono">{formatDurationMs(run.durationMs)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  <p className="control-footnote">
                    Optimistic control trades retries for parallelism. Pessimistic control removes conflicts
                    by serialising writers on the row lock — throughput becomes a function of lock hold time.
                    Both must produce the same final balances.
                  </p>
                </div>
              ) : null}

              {!history.optimistic || !history.pessimistic ? (
                <p className="compare-hint">
                  Run the {(result.strategy === 'optimistic' ? 'pessimistic' : 'optimistic')} strategy to compare the two
                  outcomes side by side.
                </p>
              ) : null}

              <InvariantsBand result={result} />

              <div className="result-grid">
                <div className="result-panel">
                  <SectionHead label="Latency" title="Per-transfer distribution" meta={result.concurrency + ' concurrent'} />
                  <LatencyBars result={result} />
                </div>

                <div className="result-panel">
                  <SectionHead label="Balances" title="After the burst" meta={formatZar(result.finalBalances.reduce((sum, account) => sum + account.balanceCents, 0))} />
                  <ul className="balances">
                    {result.finalBalances.map((account) => (
                      <li key={account.id}>
                        <span className="balance-code mono">{account.code}</span>
                        <span className="balance-label">{account.label}</span>
                        <span className="balance-value mono">{formatZar(account.balanceCents)}</span>
                      </li>
                    ))}
                  </ul>
                </div>
              </div>

              <div className="receipts-block">
                <SectionHead
                  label="Request log"
                  title="Receipts"
                  meta={result.receipts.length + ' requests'}
                />
                <table className="receipts">
                  <thead>
                    <tr>
                      <th>#</th>
                      <th>Status</th>
                      <th>Attempts</th>
                      <th>Conflicts</th>
                      <th>Latency</th>
                      <th>Reason</th>
                    </tr>
                  </thead>
                  <tbody>
                    {(showAllReceipts ? result.receipts : result.receipts.slice(0, 12)).map((receipt) => (
                      <tr key={receipt.index} className={'receipt-' + receipt.status}>
                        <td className="mono">{String(receipt.index + 1).padStart(2, '0')}</td>
                        <td>
                          <span className={'receipt-status ' + (receipt.status === 'completed' ? 'state-verified' : receipt.status === 'rejected' ? 'state-failed' : 'state-unknown')}>
                            {receipt.status}
                            {receipt.replayed ? ' · replay' : ''}
                          </span>
                        </td>
                        <td className="mono">{receipt.attempts}</td>
                        <td className="mono">{receipt.conflicts}</td>
                        <td className="mono">{formatDurationMs(receipt.durationMs)}</td>
                        <td className="mono receipt-reason">{receipt.failureReason ?? '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                {result.receipts.length > 12 ? (
                  <button type="button" className="btn btn-ghost receipts-toggle" onClick={() => setShowAllReceipts((value) => !value)}>
                    {showAllReceipts ? 'Show first 12' : 'Show all ' + result.receipts.length}
                  </button>
                ) : null}
              </div>

            </>
          ) : (
            <div className="arena-empty">
              <span className="label">No run yet</span>
              <p className="display-m">Run a burst to see what the database actually did.</p>
              <p className="prose">
                Nothing on this page is simulated. Accepted and rejected counts come from the API, conflicts
                from PostgreSQL's xmin token, latency from the server's stopwatch and integrity from SQL
                queries over the ledger.
              </p>
              <ul className="empty-list">
                <li>Try optimistic first: eight writers on two rows will collide.</li>
                <li>Then switch to pessimistic and compare conflicts and p95 latency.</li>
                <li>Try the replay mode to see five keys absorb fifty requests.</li>
              </ul>
            </div>
          )}

          <div className="ledger-block">
            <SectionHead
              label="Append-only ledger"
              title="Recent transfers"
              meta={ledger.status === 'success' ? ledger.data.integrity.completedTransfers + ' completed' : '—'}
            />
            {ledger.status === 'success' ? (
              ledger.data.transfers.length === 0 ? (
                <p className="readout-note">No transfers yet. The ledger starts empty.</p>
              ) : (
                <ul className="ledger">
                  {ledger.data.transfers.map((transfer) => (
                    <li key={transfer.transferId} className="ledger-row">
                      <span className={'ledger-status ' + (transfer.status === 'completed' ? 'state-verified' : 'state-failed')}>
                        {transfer.status}
                      </span>
                      <span className="ledger-path mono">
                        {transfer.fromCode} → {transfer.toCode}
                      </span>
                      <span className="ledger-amount mono">{formatZar(transfer.amountCents)}</span>
                      <span className="ledger-entries">
                        {transfer.entries.map((entry) => (
                          <span key={entry.direction + entry.accountCode} className={'ledger-entry ' + entry.direction}>
                            {entry.direction === 'debit' ? '−' : '+'}
                            {formatZar(entry.amountCents)} {entry.accountCode}
                          </span>
                        ))}
                      </span>
                      <span className="ledger-attempts mono">
                        {transfer.attempts} attempt{transfer.attempts === 1 ? '' : 's'}
                      </span>
                    </li>
                  ))}
                </ul>
              )
            ) : (
              <p className="readout-note">{ledger.status === 'error' ? ledger.error : 'Loading ledger…'}</p>
            )}
          </div>
        </section>
      </div>
    </article>
  )
}

function Stat({ label, value, tone }: { label: string; value: string; tone: 'verified' | 'failed' | 'muted' }) {
  return (
    <div className="stat">
      <span className="stat-value mono">{value}</span>
      <span className={'stat-label ' + (tone === 'muted' ? '' : 'state-' + tone)}>{label}</span>
    </div>
  )
}

function LatencyBars({ result }: { result: ArenaRunResult }) {
  const max = Math.max(result.latency.maxMs, 1)
  const bars = [
    { label: 'p50', value: result.latency.p50Ms },
    { label: 'p95', value: result.latency.p95Ms },
    { label: 'max', value: result.latency.maxMs },
  ]

  return (
    <div className="latency">
      {bars.map((bar) => (
        <div className="latency-row" key={bar.label}>
          <span className="latency-label mono">{bar.label}</span>
          <span className="latency-track">
            <span className="latency-fill" style={{ width: Math.max(2, (bar.value / max) * 100) + '%' }} />
          </span>
          <span className="latency-value mono">{formatDurationMs(bar.value)}</span>
        </div>
      ))}
      <div className="latency-foot">
        <span>mean</span>
        <span className="mono">{formatDurationMs(result.latency.meanMs)}</span>
      </div>
    </div>
  )
}

function InvariantsBand({ result }: { result: ArenaRunResult }) {
  const checks = [
    { ok: result.integrity.balanced, label: 'Ledger sums to zero', detail: formatZar(result.integrity.netCents) },
    {
      ok: result.integrity.ledgerEntries === result.integrity.completedTransfers * 2,
      label: 'Two entries per transfer',
      detail: result.integrity.ledgerEntries + ' entries',
    },
    { ok: result.integrity.malformedTransfers === 0, label: 'No malformed transfers', detail: result.integrity.malformedTransfers + ' found' },
    { ok: result.integrity.negativeAccounts === 0, label: 'No negative balances', detail: result.integrity.negativeAccounts + ' accounts' },
  ]

  return (
    <div className={'invariants-band ' + (result.integrity.ok ? 'invariants-ok' : 'invariants-bad')}>
      <div className="invariants-head">
        <span className="label">Ledger invariants</span>
        <span className={'mono ' + (result.integrity.ok ? 'state-verified' : 'state-failed')}>
          {result.integrity.ok ? 'held' : 'violated'}
        </span>
      </div>
      <ul className="invariants">
        {checks.map((check) => (
          <li key={check.label} className={check.ok ? 'state-verified' : 'state-failed'}>
            {check.ok ? <CheckIcon size={15} /> : <CrossIcon size={15} />}
            <span className="invariant-label">{check.label}</span>
            <span className="mono invariant-detail">{check.detail}</span>
          </li>
        ))}
      </ul>
      <p className="invariants-note">
        {result.conflicts > 0
          ? result.conflicts +
            ' conflicts and ' +
            result.totalRetries +
            ' retries are the expected cost of optimistic control. No balance moved without a committed pair of entries.'
          : 'Pessimistic control serialised the writers: no conflicts, no retries, and the same invariants hold.'}
      </p>
    </div>
  )
}
