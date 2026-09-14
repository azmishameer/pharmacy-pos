import { useEffect, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'

type StaffUser = { username: string; roles: string[] }
type Session = { csrfToken: string; user: StaffUser | null }

async function fetchSession(): Promise<Session> {
  const response = await fetch('/api/auth/session', { cache: 'no-store', signal: AbortSignal.timeout(10000) })
  if (!response.ok) throw new Error('Cannot check sign-in. Check that the updated backend and database are running.')
  return response.json() as Promise<Session>
}

export function StaffSession({ children }: {
  children: (user: StaffUser, signOut: () => void) => ReactNode
}) {
  const [session, setSession] = useState<Session | null>(null)
  const [error, setError] = useState('')
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let active = true
    void fetchSession().then(value => { if (active) setSession(value) })
      .catch((failure: Error) => { if (active) setError(failure.message) })
    const expired = () => { setSession(null); setPassword(''); setError('Your session ended. Sign in again.'); void fetchSession().then(value => { if (active) setSession(value) }).catch(() => {}) }
    window.addEventListener('session-expired', expired)
    return () => { active = false; window.removeEventListener('session-expired', expired) }
  }, [])

  async function signIn(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      const current = await fetchSession()
      const response = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': current.csrfToken },
        body: JSON.stringify({ username, password }), signal: AbortSignal.timeout(10000),
      })
      setPassword('')
      if (!response.ok) throw new Error(response.status === 429
        ? 'Too many attempts. Wait a minute before trying again.'
        : 'Unable to sign in. Check your details or try again later.')
      setSession(await fetchSession())
    } catch (failure) {
      setPassword('')
      setError(failure instanceof Error ? failure.message : 'Sign-in failed. Please try again.')
    } finally { setBusy(false) }
  }

  async function signOut() {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const current = await fetchSession()
      const response = await fetch('/api/auth/logout', {
        method: 'POST', headers: { 'X-CSRF-TOKEN': current.csrfToken }, signal: AbortSignal.timeout(10000),
      })
      if (!response.ok && response.status !== 401) throw new Error('Sign-out was not confirmed. Check the connection and try again.')
      setSession(null)
      setSession(await fetchSession())
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Sign-out failed.')
    } finally { setBusy(false) }
  }

  if (session?.user) return <>
    {error && <p className="auth-error" role="alert">{error}</p>}
    {children(session.user, () => { void signOut() })}
  </>

  return <main className="login-shell">
    <section className="login-card" aria-labelledby="sign-in-heading">
      <span className="brand-mark" aria-hidden="true">＋</span>
      <p className="eyebrow">PHARMACY POS</p>
      <h1 id="sign-in-heading">Staff sign-in</h1>
      <p className="subtitle">Use your pharmacy staff account to continue.</p>
      {!session && !error ? <p role="status">Checking your session…</p> : <form onSubmit={signIn}>
        <label htmlFor="username">Username</label>
        <input id="username" autoComplete="username" required maxLength={256} value={username} onChange={event => setUsername(event.target.value)} />
        <label htmlFor="password">Password</label>
        <input id="password" type="password" autoComplete="current-password" required maxLength={1024} value={password} onChange={event => setPassword(event.target.value)} />
        {error && <p className="auth-error" role="alert">{error}</p>}
        <button className="primary" type="submit" disabled={busy}>{busy ? 'Signing in…' : 'Sign in'}</button>
      </form>}
    </section>
  </main>
}
