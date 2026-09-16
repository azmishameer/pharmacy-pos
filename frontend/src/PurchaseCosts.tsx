import { useEffect, useRef, useState } from 'react'
import { stockGet, stockPost } from './stockApi'

type Cost = { id: string; revision: number; totalCost: number; receivedUnits: number; unitCost: number; reason: string; actorName: string; at: string }
type Delivery = { receiptId: string; brandName: string; batchNumber: string; supplier: string; deliveryReference: string; receivedDate: string; receivedUnits: number; baseUnit: string; cost: Cost | null }
type Page<T> = { items: T[]; hasMore: boolean }
const money = (v: number, digits = 2) => `৳${v.toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: digits })}`
export function PurchaseCosts({ onBack }: { onBack: () => void }) {
  const [search, setSearch] = useState(''), [page, setPage] = useState(1), [refresh, setRefresh] = useState(0)
  return <><div className="page-heading"><div><p className="eyebrow">ADMIN</p><h1>Purchase costs</h1><p>Record the total paid for each approved delivery.</p></div><button onClick={onBack}>Medicine catalogue</button></div>
    <section className="catalogue-panel charge-form"><p>Costs are private to admins. Missing costs do not prevent sales. Sold-out and disposed deliveries remain here for later cost entry.</p><label>Search medicine or batch<input type="search" maxLength={100} value={search} onChange={e => { setSearch(e.target.value); setPage(1) }} /></label><button onClick={() => setRefresh(v => v + 1)}>Refresh costs</button>
      <CostList key={`${search}-${page}-${refresh}`} search={search} page={page} onPage={setPage} onChanged={() => setRefresh(v => v + 1)} />
    </section></>
}
function CostList({ search, page, onPage, onChanged }: { search: string; page: number; onPage: (p: number) => void; onChanged: () => void }) {
  const [data, setData] = useState<Page<Delivery> | null>(null), [error, setError] = useState('')
  useEffect(() => { const controller = new AbortController(); const timer = window.setTimeout(() => void stockGet<Page<Delivery>>(`/api/purchase-costs?${new URLSearchParams({ search, page: String(page) })}`, controller.signal).then(setData).catch(e => { if (!controller.signal.aborted) setError(e.message) }), 200); return () => { controller.abort(); window.clearTimeout(timer) } }, [search, page])
  if (error) return <p role="alert">{error}</p>
  if (!data) return <p>Loading delivery costs…</p>
  return <>{data.items.length === 0 && <p>No approved deliveries found.</p>}{data.items.map(d => <CostCard key={d.receiptId} delivery={d} onChanged={onChanged} />)}<nav className="pagination"><button disabled={page === 1} onClick={() => onPage(page - 1)}>Previous</button><span>Page {page}</span><button disabled={!data.hasMore} onClick={() => onPage(page + 1)}>Next</button></nav></>
}
function CostCard({ delivery: d, onChanged }: { delivery: Delivery; onChanged: () => void }) {
  const [editing, setEditing] = useState(false), [history, setHistory] = useState(false)
  return <article className="counter-product"><h2>{d.brandName} · Batch {d.batchNumber}</h2><p>{d.supplier} · Delivery reference: {d.deliveryReference || '—'} · Received {d.receivedDate}</p><p>Original delivery: {d.receivedUnits.toLocaleString()} {d.baseUnit.toLowerCase()}s</p>
    <p><strong>Purchase cost: {d.cost ? money(d.cost.totalCost) : 'Not entered'}</strong>{d.cost && <> · approximately {money(d.cost.unitCost, 6)} per {d.baseUnit.toLowerCase()} · Revision {d.cost.revision}</>}</p>
    <div className="heading-actions"><button onClick={() => setEditing(v => !v)}>{editing ? 'Cancel cost entry' : d.cost ? 'Correct purchase cost' : 'Enter purchase cost'}</button><button onClick={() => setHistory(v => !v)}>{history ? 'Hide cost history' : 'View cost history'}</button></div>
    {editing && <CostForm delivery={d} onChanged={onChanged} />}{history && <CostHistory id={d.receiptId} />}
  </article>
}
function CostForm({ delivery: d, onChanged }: { delivery: Delivery; onChanged: () => void }) {
  const [amount, setAmount] = useState(d.cost ? String(d.cost.totalCost) : ''), [reason, setReason] = useState(''), [checked, setChecked] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const request = useRef(crypto.randomUUID())
  function edited() { request.current = crypto.randomUUID(); setChecked(false) }
  return <form onSubmit={async e => { e.preventDefault(); setError(''); if (!checked || amount.trim() === '') return; setBusy(true); try { await stockPost(`/api/purchase-costs/${d.receiptId}`, { requestId: request.current, expectedId: d.cost?.id ?? null, totalCost: Number(amount), reason }); onChanged() } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}><fieldset disabled={busy} className="charge-fields">
    <label>Total purchase cost for this delivery (BDT)<input type="number" required min="0" max="1000000000000" step="0.01" value={amount} onChange={e => { setAmount(e.target.value); edited() }} /></label>
    <p>This is the total paid for all {d.receivedUnits.toLocaleString()} original units, not the amount for one piece or the remaining stock. Include supplier discounts in this total.</p>
    {amount !== '' && Number(amount) >= 0 && <p>Approximate unit cost: {money(Number(amount) / d.receivedUnits, 6)} per {d.baseUnit.toLowerCase()}</p>}
    <label>{d.cost ? 'Reason for correction' : 'Cost note or invoice reference'}<textarea required maxLength={1000} value={reason} onChange={e => { setReason(e.target.value); edited() }} /></label>
    <label><input type="checkbox" required checked={checked} onChange={e => setChecked(e.target.checked)} /> I confirm this cost covers the entire original delivery{Number(amount) === 0 && amount !== '' ? ' and that it was received at zero cost' : ''}.</label>
    <button className="primary" disabled={!checked}>{busy ? 'Saving…' : 'Save purchase cost'}</button>
  </fieldset>{error && <p role="alert" className="auth-error">{error}</p>}</form>
}
function CostHistory({ id }: { id: string }) {
  const [page, setPage] = useState(1)
  return <HistoryPage key={page} id={id} page={page} onPage={setPage} />
}
function HistoryPage({ id, page, onPage }: { id: string; page: number; onPage: (p: number) => void }) {
  const [data, setData] = useState<Page<Cost> | null>(null), [error, setError] = useState('')
  useEffect(() => { const c = new AbortController(); void stockGet<Page<Cost>>(`/api/purchase-costs/${id}/history?page=${page}`, c.signal).then(setData).catch(e => { if (!c.signal.aborted) setError(e.message) }); return () => c.abort() }, [id, page])
  if (error) return <p role="alert">{error}</p>
  if (!data) return <p>Loading cost history…</p>
  return <div><h3>Cost history</h3>{!data.items.length && <p>No purchase cost entered.</p>}{data.items.map(c => <p key={c.id}>Revision {c.revision} · {money(c.totalCost)} · {c.actorName} · {new Date(c.at).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' })} (Bangladesh time) · {c.reason}</p>)}<div className="pagination"><button disabled={page === 1} onClick={() => onPage(page - 1)}>Earlier page</button><span>Page {page}</span><button disabled={!data.hasMore} onClick={() => onPage(page + 1)}>Older costs</button></div></div>
}
