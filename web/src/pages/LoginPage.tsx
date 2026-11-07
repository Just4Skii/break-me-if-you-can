import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError, postJson } from '../api'
import { clearStoredPlayer, getStoredPlayer, setStoredPlayer } from '../auth'
import type { AuthResponse } from '../types'

export function LoginPage() {
  const [stored, setStored] = useState(getStoredPlayer())
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const next = params.get('next') ?? '/performance'

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)

    try {
      const path = mode === 'register' ? '/api/players/register' : '/api/players/login'
      const data = await postJson<AuthResponse>(path, { username, password })
      setStoredPlayer({ token: data.token, username: data.player.username })
      setStored({ token: data.token, username: data.player.username })
      navigate(next, { replace: true })
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'The account request could not be completed.')
    } finally {
      setBusy(false)
    }
  }

  const signOut = () => {
    clearStoredPlayer()
    setStored(null)
    setPassword('')
  }

  if (stored) {
    return (
      <div className="competition">
        <header className="comp-head">
          <span className="label">05 / Player account</span>
          <h1 className="comp-title">Signed in.</h1>
          <p className="comp-lede">
            Your ranked runs are saved as <strong>{stored.username}</strong>. The leaderboard is public;
            your password is never shown or stored in the browser.
          </p>
        </header>

        <section className="comp-panel">
          <div className="comp-panel-head">
            <div>
              <span className="label">Account</span>
              <h2>{stored.username}</h2>
            </div>
            <span className="comp-status">signed in</span>
          </div>
          <div className="comp-actions">
            <Link className="comp-button" to="/performance">My performance →</Link>
            <Link className="comp-button secondary" to="/leaderboard">Public leaderboard</Link>
            <button className="comp-button secondary" type="button" onClick={signOut}>Sign out</button>
          </div>
        </section>
      </div>
    )
  }

  return (
    <div className="competition">
      <header className="comp-head">
        <span className="label">05 / Player account</span>
        <h1 className="comp-title">Keep your best run.</h1>
        <p className="comp-lede">
          The arena and the leaderboard are public. An account is only needed to save a verified run,
          take a place on the board and compare your personal best. There is no email step and no
          personal data beyond a username and a password hash.
        </p>
      </header>

      <section className="comp-panel comp-auth-panel">
        <div className="comp-panel-head">
          <div>
            <span className="label">{mode === 'register' ? 'Create an account' : 'Sign in'}</span>
            <h2>{mode === 'register' ? 'Choose a username.' : 'Welcome back.'}</h2>
          </div>
          <div className="comp-mode" role="tablist" aria-label="Account mode">
            <button
              type="button"
              className={'comp-mode-button' + (mode === 'login' ? ' active' : '')}
              onClick={() => { setMode('login'); setError(null) }}
            >
              Sign in
            </button>
            <button
              type="button"
              className={'comp-mode-button' + (mode === 'register' ? ' active' : '')}
              onClick={() => { setMode('register'); setError(null) }}
            >
              Create
            </button>
          </div>
        </div>

        <form className="comp-form" onSubmit={submit}>
          <label className="comp-field">
            <span className="label">Username</span>
            <input
              className="comp-input"
              name="username"
              autoComplete="username"
              minLength={3}
              maxLength={24}
              pattern="[A-Za-z0-9_-]{3,24}"
              required
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              placeholder="3–24 letters, numbers, _ or -"
            />
          </label>

          <label className="comp-field">
            <span className="label">Password</span>
            <input
              className="comp-input"
              name="password"
              type="password"
              autoComplete={mode === 'register' ? 'new-password' : 'current-password'}
              minLength={8}
              maxLength={128}
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              placeholder="At least 8 characters"
            />
          </label>

          {error ? <p className="comp-error" role="alert">{error}</p> : null}

          <button className="comp-button" type="submit" disabled={busy}>
            {busy ? 'Working…' : mode === 'register' ? 'Create account' : 'Sign in'}
          </button>
          <p className="comp-note">
            {mode === 'register'
              ? 'Passwords are hashed with PBKDF2-SHA256 and unique salts. There is no email or recovery flow in this release.'
              : 'Use the username and password you registered with.'}
          </p>
        </form>
      </section>
    </div>
  )
}
