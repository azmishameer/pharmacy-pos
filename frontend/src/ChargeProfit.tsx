import { useState, useRef } from 'react'
import { stockGet, stockPost } from './stockApi'
const profitLabel = (v: boolean | null) => v === null ? 'Needs profit review' : v ? 'Excluded from profit' : 'Included as pharmacy income'
type History = { initialExcludeFromProfit: boolean | null; createdBy: string; createdAt: string; items: { id: string; previousValue: boolean | null; excludeFromProfit: boolean; actorName: string; at: string; reason: string }[]; hasMore: boolean }
export function ChargeProfit({ id, value, version, onSaved }: { id: string; value: boolean | null; version: string | null; onSaved: () => void }) {
  const [editing, setEditing] = useState(false), [exclude, setExclude] = useState(value ?? false), [reason, setReason] = useState(''), [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState(''), [history, setHistory] = useState<History | null>(null), [page, setPage] = useState(1)
  const request = useRef(crypto.randomUUID())
  function edited() { request.current = crypto.randomUUID(); setConfirmed(false) }
  async function loadHistory(n: number) { setError(''); try { setHistory(await stockGet<History>(`/api/charges/${id}/profit-history?page=${n}`)); setPage(n) } catch (e) { setError((e as Error).message) } }
  return <div><p><strong>{profitLabel(value)}</strong></p><div className="heading-actions"><button disabled={busy} onClick={() => setEditing(v => !v)}>{editing ? 'Cancel classification' : value === null ? 'Review profit treatment' : 'Change profit treatment'}</button><button disabled={busy} onClick={() => void loadHistory(1)}>Profit classification history</button></div>
    {editing && <form onSubmit={async e => { e.preventDefault(); if (!confirmed) return; setBusy(true); setError(''); try { await stockPost(`/api/charges/${id}/profit-classification`, { requestId: request.current, expectedVersion: version, excludeFromProfit: exclude, reason }); onSaved() } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}><fieldset disabled={busy} className="charge-fields">
      <label><input type="checkbox" checked={exclude} onChange={e => { setExclude(e.target.checked); edited() }} /> Exclude this charge from profit</label><p>Check for VAT, tax or other money collected for another party. Leave unchecked for pharmacy income. This changes reporting treatment for this charge, including past sales; customer totals and receipts remain unchanged.</p>
      <label>Reason for classification<textarea required maxLength={1000} value={reason} onChange={e => { setReason(e.target.value); edited() }} /></label>
      <label><input type="checkbox" required checked={confirmed} onChange={e => setConfirmed(e.target.checked)} /> I confirm: {exclude ? 'exclude from profit' : 'include as pharmacy income'}.</label><button className="primary" disabled={!confirmed}>{busy ? 'Saving…' : 'Save profit treatment'}</button>
    </fieldset></form>}
    {error && <p role="alert">{error}</p>}{history && <div><p>At creation: {profitLabel(history.initialExcludeFromProfit)} · {history.createdBy} · {new Date(history.createdAt).toLocaleString()}</p>{history.items.map(h => <p key={h.id}>{profitLabel(h.previousValue)} → {profitLabel(h.excludeFromProfit)} · {h.actorName} · {new Date(h.at).toLocaleString()} · {h.reason}</p>)}<button disabled={page === 1} onClick={() => void loadHistory(page - 1)}>Previous history page</button><span> Page {page} </span><button disabled={!history.hasMore} onClick={() => void loadHistory(page + 1)}>Older classification history</button></div>}
  </div>
}
