import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useApi } from '../hooks/useApi'
import type { ChallengeView, LeaderboardView } from '../types'
import { formatDurationMs, formatNumber, formatTimestamp } from '../format'

export function LeaderboardPage() {
  const challenges = useApi<ChallengeView[]>('/api/challenges')
  const [selected, setSelected] = useState('optimistic-8')
  const board = useApi<LeaderboardView>('/api/leaderboard?challenge=' + encodeURIComponent(selected))

  const challenge = challenges.status === 'success'
    ? challenges.data.find((item) => item.key === selected)
    : undefined

  return (
    <div className="competition">
      <header className="comp-head">
        <span className="label">03 / Public leaderboard</span>
        <h1 className="comp-title">Verified runs only.</h1>
        <p className="comp-lede">
          Every entry is a challenge the server executed itself. A run appears here only when the
          ledger balanced, every requested transfer completed and nobody was rejected. No account is
          needed to look.
        </p>
      </header>

      <div className="comp-tabs" role="tablist" aria-label="Challenge">
        {challenges.status === 'success'
          ? challenges.data.map((item) => (
              <button
                key={item.key}
                type="button"
                role="tab"
                aria-selected={item.key === selected}
                className={'comp-tab' + (item.key === selected ? ' active' : '')}
                onClick={() => setSelected(item.key)}
              >
                <span className="comp-tab-name">{item.name}</span>
                <span className="comp-tab-meta">{item.requests} transfers · {item.concurrency} at once</span>
              </button>
            ))
          : <span className="comp-note">Loading challenges…</span>}
      </div>

      <section className="comp-panel" aria-live="polite">
        <div className="comp-panel-head">
          <div>
            <span className="label">{challenge ? challenge.strategy : 'Challenge'}</span>
            <h2>{challenge ? challenge.name : 'Leaderboard'}</h2>
            {challenge ? <p className="comp-note">{challenge.summary}</p> : null}
          </div>
          <Link className="comp-button secondary" to="/arena">Run a challenge</Link>
        </div>

        {board.status === 'error' ? (
          <p className="comp-error" role="alert">{board.error}</p>
        ) : board.status === 'loading' ? (
          <p className="comp-note">Loading the board…</p>
        ) : board.data.entries.length === 0 ? (
          <div className="comp-empty">
            <p>No verified runs on this challenge yet.</p>
            <Link className="comp-button" to="/arena">Be the first →</Link>
          </div>
        ) : (
          <div className="comp-table-wrap">
            <table className="comp-table">
              <thead>
                <tr>
                  <th scope="col">#</th>
                  <th scope="col">Player</th>
                  <th scope="col">Score</th>
                  <th scope="col">Time</th>
                  <th scope="col">Accepted</th>
                  <th scope="col">Conflicts</th>
                  <th scope="col">Achieved</th>
                </tr>
              </thead>
              <tbody>
                {board.data.entries.map((entry) => (
                  <tr key={entry.rank + entry.username}>
                    <td className="comp-rank">{String(entry.rank).padStart(2, '0')}</td>
                    <td><strong>{entry.username}</strong></td>
                    <td className="mono">{formatNumber(entry.score)}</td>
                    <td className="mono">{formatDurationMs(entry.durationMs)}</td>
                    <td className="mono">{entry.accepted}</td>
                    <td className="mono">{entry.conflicts}</td>
                    <td className="mono comp-when">{formatTimestamp(entry.achievedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <p className="comp-note">
        Scores are computed by the server from the completed run and are never supplied by the client.
        One entry per player: the board shows each player's best verified run.
      </p>
    </div>
  )
}
