import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { stockGet, stockPost } from './stockApi'
import type { Receipt } from './stockApi'

type Change = { id: string; at: string; admin: string; change: { oldAmount: number; oldUnit: string; oldUnits: number; newAmount: number; newUnit: string; newUnits: number; reason: string } }
export function CorrectMrp({ row, onSaved, onCancel }: { row: Receipt; onSaved: () => void; onCancel: () => void }) {
  const [amount, setAmount] = useState(String(row.mrpAmount)), [unit, setUnit] = useState(row.mrpUnit)
  const [reason, setReason] = useState(''), [checked, setChecked] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const pending = useRef<{ payload: string; id: string } | null>(null)
  const lock = useRef(false)
  const denominator = unit === 'Piece' ? 1 : unit === 'Strip' ? row.unitsPerStrip : row.unitsPerBox
  async function submit(e: FormEvent) {
    e.preventDefault(); if (lock.current) return
    lock.current = true; setBusy(true); setError('')
    const details = { revisionId: row.id, expectedAmount: row.mrpAmount, expectedUnit: row.mrpUnit, expectedUnits: row.mrpUnits, amount: Number(amount), unit, reason: reason.trim(), checkedPackaging: checked }
    const payload = JSON.stringify(details)
    if (pending.current?.payload !== payload) pending.current = { payload, id: crypto.randomUUID() }
    try {
      const saved = await stockPost<{ correctionId: string }>(`/api/stock/receipts/${row.receiptId}/correct-mrp`, { ...details, requestId: pending.current.id })
      if (saved.correctionId !== pending.current.id) throw new Error('Correction was not confirmed. Keep the form unchanged and retry.')
      onSaved()
    } catch (e) { setError(e instanceof Error ? e.message : 'Correction was not confirmed.') }
    finally { lock.current = false; setBusy(false) }
  }
  return <form onSubmit={submit} className="history-entry"><h3>Correct recorded MRP</h3>
    <p>Currently ৳{row.mrpAmount.toFixed(2)} per {row.mrpUnit === 'Piece' ? row.baseUnit.toLowerCase() : row.mrpUnit.toLowerCase()}. This corrects a recording mistake and keeps the stock quantity unchanged.</p>
    <fieldset disabled={busy}><legend className="sr-only">MRP correction</legend><div className="entry-grid">
      <div className="field"><label htmlFor={`correct-amount-${row.id}`}>Correct MRP amount (৳)</label><input id={`correct-amount-${row.id}`} type="number" min="0.01" max="1000000000" step="0.01" required value={amount} onChange={e => setAmount(e.target.value)} /></div>
      <div className="field"><label htmlFor={`correct-unit-${row.id}`}>Correct MRP unit</label><select id={`correct-unit-${row.id}`} value={unit} onChange={e => setUnit(e.target.value)}><option value="Piece">One {row.baseUnit.toLowerCase()}</option>{row.unitsPerStrip > 0 && <option value="Strip">One strip</option>}<option value="Box">One box</option></select></div>
    </div>
    <p role="status">৳{Number(amount).toFixed(2)} for {denominator} {row.baseUnit.toLowerCase()}s ≈ ৳{(Number(amount) / denominator).toFixed(6).replace(/\.?0+$/, '')} per {row.baseUnit.toLowerCase()}.</p>
    <div className="field"><label htmlFor={`correct-reason-${row.id}`}>Reason for correction</label><input id={`correct-reason-${row.id}`} required maxLength={100} value={reason} onChange={e => setReason(e.target.value)} /></div>
    <p><label><input type="checkbox" required checked={checked} onChange={e => setChecked(e.target.checked)} /> I checked the corrected amount and unit against the packaging</label></p>
    {error && <p role="alert" className="auth-error">{error}</p>}<div className="entry-actions"><button type="button" onClick={onCancel}>Cancel price correction</button><button className="primary" type="submit">{busy ? 'Saving…' : 'Save corrected MRP'}</button></div>
    </fieldset>
  </form>
}
export function MrpHistory({ receiptId }: { receiptId: string }) {
  const [rows, setRows] = useState<Change[] | null>(null), [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    void stockGet<Change[]>(`/api/stock/receipts/${receiptId}/mrp-history`, controller.signal).then(setRows).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    return () => controller.abort()
  }, [receiptId])
  return <div><h4>MRP correction history</h4>{error && <p role="alert">{error}</p>}{rows?.length === 0 && <p>No MRP corrections.</p>}{rows?.map(r => <p key={r.id}>৳{r.change.oldAmount.toFixed(2)} per {r.change.oldUnit.toLowerCase()} ({r.change.oldUnits} units) → ৳{r.change.newAmount.toFixed(2)} per {r.change.newUnit.toLowerCase()} ({r.change.newUnits} units). {r.change.reason} — {r.admin}, {new Date(r.at).toLocaleString()}</p>)}</div>
}
