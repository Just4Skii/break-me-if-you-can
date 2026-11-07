import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { clearStoredPlayer, getStoredPlayer } from '../auth'
import { useApi } from '../hooks/useApi'
import type { PerformanceView } from '../types'
import { formatDurationMs, formatNumber, formatTimestamp } from '../format'

export function PerformancePage() {
  const [stored, setStored] = useState(getStoredPlayer())
  const performance = useApi<PerformanceView>(stored ? '/api/players/me/performance' : null)
  const navigate = useNavigate()

  const signOut = () => {
    clearStoredPlayer()
    setStored(null)
    navigate('/login', { replace: true })
  }

  if (!stored) {
    return (
      <div className="competition">
        <header className="comp-head">
          <span className="label">04 / My performance</span>
          <h1 className="comp-title">Measure yourself.</h1>
          <p className="comp-lede">
            Sign in to keep a personal best on each challenge and a history of your verified runs.
            The leaderboard stays public either way.
          </p>
        </header>
        <section className="comp-panel comp-empty">
          <p>You are not signed in.</p>
          <Link className="comp-button" to="/login?next=/performance">Sign in or create an account →</Link>
        </section>
      </div>
    )
  }

  if (performance.status === 'error') {
    return (
      <div className="competition">
        <header className="comp-head">
          <span className="label">04 / My performance</span>
          <h1 className="comp-title">Session expired.</h1>
          <p className="comp-lede">Sign in again to load your performance.</p>
        </header>
        <section className="comp-panel comp-empty">
          <p className="comp-error" role="alert">{performance.error}</p>
          <Link className="comp-button" to="/login?next=/performance">Sign in again →</Link>
        </section>
      </div>
    )
  }

  if (performance.status !== 'success') {
    return (
      <div className="competition">
        <p className="comp-note">Loading your runs…</p>
      </div>
    )
  }

  const data = performance.data

  return (
    <div className="competition">
      <header className="comp-head">
        <span className="label">04 / My performance</span>
        <h1 className="comp-title">{data.player.username}.</h1>
        <p className="comp-lede">
          {data.verifiedRuns} verified {data.verifiedRuns === 1 ? 'run' : 'runs'} out of {data.totalRuns} attempted.
          A run counts only when the server verified the ledger.
        </p>
      </header>

      <section className="comp-panel">
        <div className="comp-panel-head">
          <div>
            <span className="label">Personal bests</span>
            <h2>One per challenge</h2>
          </div>
          <button className="comp-button secondary" type="button" onClick={signOut}>Sign out</button>
        </div>
        {data.bests.length === 0 ? (
          <div className="comp-empty">
            <p>No verified run yet.</p>
            <Link className="comp-button" to="/arena">Run a ranked challenge →</Link>
          </div>
        ) : (
          <div className="comp-table-wrap">
            <table className="comp-table">
              <thead>
                <tr>
                  <th scope="col">Challenge</th>
                  <th scope="col">Best score</th>
                  <th scope="col">Time</th>
                  <th scope="col">Achieved</th>
                </tr>
              </thead>
              <tbody>
                {data.bests.map((best) => (
                  <tr key={best.challengeKey}>
                    <td><strong>{best.challengeName}</strong></td>
                    <td className="mono">{formatNumber(best.score)}</td>
                    <td className="mono">{formatDurationMs(best.durationMs)}</td>
                    <td className="mono comp-when">{formatTimestamp(best.achievedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="comp-panel">
        <div className="comp-panel-head">
          <div>
            <span className="label">Recent runs</span>
            <h2>Last {data.recent.length}</h2>
          </div>
          <Link className="comp-button secondary" to="/leaderboard">Public leaderboard</Link>
        </div>
        {data.recent.length === 0 ? (
          <p className="comp-note">Nothing recorded yet.</p>
        ) : (
          <div className="comp-table-wrap">
            <table className="comp-table">
              <thead>
                <tr>
                  <th scope="col">Challenge</th>
                  <th scope="col">Score</th>
                  <th scope="col">Verified</th>
                  <th scope="col">Accepted</th>
                  <th scope="col">Time</th>
                  <th scope="col">When</th>
                </tr>
              </thead>
              <tbody>
                {data.recent.map((run, index) => (
                  <tr key={run.challengeKey + run.createdAt + index}>
                    <td><strong>{run.challengeName}</strong></td>
                    <td className="mono">{formatNumber(run.score)}</td>
                    <td>
                      <span className={'comp-verified ' + (run.verified ? 'ok' : 'bad')}>
                        {run.verified ? 'verified' : 'not ranked'}
                      </span>
                    </td>
                    <td className="mono">{run.accepted} / {run.accepted + run.rejected}</td>
                    <td className="mono">{formatDurationMs(run.durationMs)}</td>
                    <td className="mono comp-when">{formatTimestamp(run.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
