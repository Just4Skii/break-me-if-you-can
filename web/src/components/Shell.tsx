import { useEffect } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useApi } from '../hooks/useApi'
import type { HealthReport, Meta } from '../types'
import { shortSha } from '../format'
import { Logo } from './Logo'
import { GithubIcon } from './Icons'

const NAV = [
  { to: '/proof', index: '01', label: 'Proof', description: 'Release evidence' },
  { to: '/arena', index: '02', label: 'Arena', description: 'Concurrency lab' },
  { to: '/tour', index: '03', label: 'Tour', description: 'How it works' },
]

export function Shell() {
  const meta = useApi<Meta>('/api/meta')
  const health = useApi<HealthReport>('/healthz')

  useEffect(() => {
    const timer = window.setInterval(() => health.reload(), 30_000)
    return () => window.clearInterval(timer)
  }, [health.reload])

  const commit = meta.status === 'success' ? meta.data.shortCommit : 'unknown'
  const repositoryUrl = meta.status === 'success' ? meta.data.repositoryUrl : undefined
  const commitUrl = repositoryUrl && commit !== 'unknown' ? repositoryUrl + '/commit/' + commit : undefined

  const live =
    health.status === 'success'
      ? { word: health.data.status, tone: health.data.status === 'healthy' ? 'verified' : 'failed' }
      : health.status === 'error'
        ? { word: 'unreachable', tone: 'failed' }
        : { word: 'connecting', tone: 'unknown' }

  const navLinks = (className: string) =>
    NAV.map((item) => (
      <NavLink
        key={item.to}
        to={item.to}
        className={({ isActive }) => className + (isActive ? ' active' : '')}
      >
        <span className="nav-index">{item.index}</span>
        <span className="nav-text">
          <span className="nav-label">{item.label}</span>
          <span className="nav-desc">{item.description}</span>
        </span>
      </NavLink>
    ))

  return (
    <div className="shell">
      <aside className="rail">
        <NavLink to="/proof" className="brand" aria-label="Break Me If You Can, home">
          <Logo />
          <span className="brand-text">
            <span className="brand-name">
              Break Me
              <br />
              If You Can
            </span>
            <span className="brand-sub label">Engineering proof lab</span>
          </span>
        </NavLink>

        <nav className="nav" aria-label="Sections">
          {navLinks('nav-item')}
        </nav>

        <div className="rail-foot">
          <div className={'live live-' + live.tone}>
            <span className="dot" />
            <span>API {live.word}</span>
          </div>
          <div className="rail-meta">
            {commitUrl ? (
              <a className="rail-link" href={commitUrl} target="_blank" rel="noreferrer">
                commit {commit}
              </a>
            ) : (
              <span>commit {commit}</span>
            )}
            <span>{meta.status === 'success' ? meta.data.runtime : 'dotnet'}</span>
          </div>
          {repositoryUrl ? (
            <a className="rail-repo" href={repositoryUrl} target="_blank" rel="noreferrer">
              <GithubIcon size={13} />
              <span>source</span>
            </a>
          ) : null}
        </div>
      </aside>

      <div className="stage">
        <header className="topbar">
          <NavLink to="/proof" className="topbar-brand">
            <Logo size={24} />
            <span>Break Me If You Can</span>
          </NavLink>
          <nav className="topbar-nav" aria-label="Sections">
            {navLinks('topnav-item')}
          </nav>
        </header>

        <main className="content">
          <Outlet />
        </main>

        <footer className="colophon">
          <span>An interactive engineering proof lab. Every number is machine-generated.</span>
          <span className="mono colophon-meta">
            {shortSha(commit)} · {meta.status === 'success' ? meta.data.environment : 'loading'}
          </span>
        </footer>
      </div>
    </div>
  )
}
