import { useEffect, useRef, useState } from 'react'
import { stockGet, stockPost } from './stockApi'
type State = { paused: boolean; version: string; workerEnabled: boolean; runs: { id: string; dueDate: string; status: string; startedAt: string; finishedAt: string | null; archive: string | null; error: string | null }[]; history: { paused: boolean; actorName: string; reason: string; at: string }[] }
const time = (v: string) => new Date(v).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' })
export function BackupSettings({ onBack }: { onBack: () => void }) {
  const [data, setData] = useState<State | null>(null), [error, setError] = useState(''), [refresh, setRefresh] = useState(0)
  useEffect(() => { const c = new AbortController(); const load = () => void stockGet<State>('/api/backups', c.signal).then(d => { setData(d); setError('') }).catch(e => { if (!c.signal.aborted) setError(e.message) }); load(); const timer = window.setInterval(load, 15000); return () => { c.abort(); window.clearInterval(timer) } }, [refresh])
  return <><div className="page-heading"><div><p className="eyebrow">ADMIN</p><h1>Database backups</h1><p>Daily at 2:00 AM · Bangladesh time (Asia/Dhaka)</p></div><button onClick={onBack}>Medicine catalogue</button></div>
    {error && <p role="alert" className="auth-error">{error}</p>}{!data ? <p>Loading backup status…</p> : <>
      <section className="catalogue-panel charge-form"><h2>{data.paused ? 'Automatic backups paused' : data.workerEnabled ? 'Automatic backups enabled' : 'Backup worker disabled on this server'}</h2>
        <p>The pharmacy server must be running. If it starts or resumes after 2 AM and today’s backup is missing, a backup runs within about one minute. Before 2 AM, it waits until 2 AM. Failed runs retry after 15 minutes while enabled.</p>
        <p>Pausing prevents new automatic backups. A backup already in progress finishes safely. Manual backups are still available.</p>
        <ScheduleForm key={data.version} paused={data.paused} version={data.version} onSaved={() => setRefresh(v => v + 1)} />
        <p>Backups are stored on the server. Keep an additional copy on a separate device. No backups are automatically deleted.</p>
      </section>
      <section className="catalogue-panel charge-form"><h2>Recent automatic backups</h2><button onClick={() => setRefresh(v => v + 1)}>Refresh backup status</button><p>Latest 20 runs. A completed backup means the archive was created; restore verification is a separate check.</p>{data.runs.length === 0 && <p>No automatic backups recorded yet.</p>}{data.runs.map(r => <article className="counter-product" key={r.id}><h3>{r.dueDate} · {r.status}</h3><p>Started {time(r.startedAt)}{r.finishedAt && ` · Finished ${time(r.finishedAt)}`} (Bangladesh time)</p>{r.archive && <p>Archive: {r.archive}</p>}{r.error && <p role="alert">{r.error}</p>}</article>)}</section>
      <section className="catalogue-panel charge-form"><h2>Pause and resume history</h2><p>Latest 20 changes.</p>{data.history.length === 0 && <p>No schedule changes recorded.</p>}{data.history.map((h, i) => <p key={i}>{h.paused ? 'Paused' : 'Resumed'} · {h.actorName} · {time(h.at)} · {h.reason}</p>)}</section>
    </>}
  </>
}
function ScheduleForm({ paused, version, onSaved }: { paused: boolean; version: string; onSaved: () => void }) {
  const [reason, setReason] = useState(''), [checked, setChecked] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const request = useRef(crypto.randomUUID())
  return <form onSubmit={async e => { e.preventDefault(); if (!checked) return; setBusy(true); setError(''); try { await stockPost('/api/backups/schedule', { requestId: request.current, expectedVersion: version, paused: !paused, reason }); onSaved() } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}><fieldset disabled={busy} className="charge-fields"><label>Reason<textarea required maxLength={1000} value={reason} onChange={e => { setReason(e.target.value); request.current = crypto.randomUUID(); setChecked(false) }} /></label><label><input type="checkbox" required checked={checked} onChange={e => setChecked(e.target.checked)} /> I confirm I want to {paused ? 'resume' : 'pause'} automatic backups.</label><button disabled={!checked} className="primary">{busy ? 'Saving…' : paused ? 'Resume automatic backups' : 'Pause automatic backups'}</button></fieldset>{error && <p role="alert">{error}</p>}</form>
}
