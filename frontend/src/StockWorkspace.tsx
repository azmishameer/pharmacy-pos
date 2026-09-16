import { useEffect, useRef, useState } from 'react'
import { ReceiveStock } from './ReceiveStock'
import { CorrectMrp, MrpHistory } from './CorrectMrp'
import { packBreakdown, stockExport, stockGet, stockPost } from './stockApi'
import type { Receipt } from './stockApi'

type Lot = { lotId: string; brandName: string; manufacturer: string; batchNumber: string; expiryDate: string; baseUnit: string; receivedUnits: number; onHandUnits: number; sellableUnits: number; availability: string; unitsPerStrip: number; unitsPerBox: number; mrpAmount: number; mrpUnit: string; mrpUnits: number; mrpVerified: boolean }
type Disposal = { id: string; lotId: string; brandName: string; batchNumber: string; expiryDate: string; quantity: number; baseUnit: string; reason: string; disposedBy: string; disposedAt: string; visibleUntil: string }
type List<T> = { items: T[]; page: number; hasMore: boolean }

export function StockWorkspace({ isAdmin, initialMedicineId, onBack }: { isAdmin: boolean; initialMedicineId?: string; onBack: () => void }) {
  const [form, setForm] = useState<{ medicineId: string; original?: Receipt } | null>(initialMedicineId ? { medicineId: initialMedicineId } : null)
  const [tab, setTab] = useState('inventory')
  const [filter, setFilter] = useState('all')
  const [page, setPage] = useState(1)
  const [refresh, setRefresh] = useState(0)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [exporting, setExporting] = useState(false)
  const reload = () => setRefresh(v => v + 1)
  function switchTab(next: string) { setTab(next); setPage(1); setFilter('all'); setError('') }
  async function download() {
    setExporting(true); setError('')
    try { await stockExport(tab === 'disposals' ? '/api/stock/disposals/export' : '/api/stock/receipts/export', tab === 'disposals' ? 'disposed-stock.csv' : 'pending-stock.csv') }
    catch (e) { setError(e instanceof Error ? e.message : 'Export failed.') }
    finally { setExporting(false) }
  }
  return <>
    <div className="page-heading"><div><p className="eyebrow">INVENTORY</p><h1>Stock & receiving</h1></div><button onClick={onBack}>Back to medicines</button></div>
    {message && <p role="status" className="save-confirmation">{message}</p>}
    {form ? <ReceiveStock key={form.original?.id ?? form.medicineId} {...form} isAdmin={isAdmin} onCancel={() => setForm(null)} onSaved={text => { setMessage(text); setForm(null); switchTab('receipts'); reload() }} /> : <>
      <p className="field-help">To receive a delivery, find its medicine in the catalogue and select Receive stock.</p>
      <nav className="stock-tabs" aria-label="Stock pages">
        <button aria-pressed={tab === 'inventory'} onClick={() => switchTab('inventory')}>Inventory</button>
        <button aria-pressed={tab === 'receipts'} onClick={() => switchTab('receipts')}>{isAdmin ? 'Receiving & review' : 'My stock entries'}</button>
        {isAdmin && <button aria-pressed={tab === 'disposals'} onClick={() => switchTab('disposals')}>Disposal history</button>}
        <button onClick={reload}>Refresh stock</button>
      </nav>
      <div className="stock-filters">
        {tab !== 'disposals' && <div className="field"><label htmlFor="stock-filter">Show</label><select id="stock-filter" value={filter} onChange={e => { setFilter(e.target.value); setPage(1) }}>
          <option value="all">{tab === 'inventory' ? 'All approved stock' : 'All entries'}</option>
          {tab === 'inventory' ? <><option value="available">Sellable stock</option><option value="held">Unsellable stock</option></> : <><option value="pending">Pending or returned entries</option><option value="price">MRP verification pending</option></>}
        </select></div>}
        {isAdmin && tab !== 'inventory' && <button disabled={exporting} onClick={() => void download()}>{exporting ? 'Exporting…' : tab === 'disposals' ? 'Export disposal CSV' : 'Export pending stock CSV'}</button>}
      </div>
      {error && <p role="alert" className="auth-error">{error}</p>}
      {tab === 'disposals' && <p>Records stay in this list for three calendar months after disposal. Export them here during that time.</p>}
      <StockList key={`${tab}-${filter}-${page}-${refresh}`} tab={tab} filter={filter} page={page} isAdmin={isAdmin} onPage={setPage} reload={reload} onCorrect={row => setForm({ medicineId: row.medicineId, original: row })} />
      <p className="page-note">Stock marked unsellable cannot be sold.</p>
    </>}
  </>
}
function StockList({ tab, filter, page, isAdmin, onPage, reload, onCorrect }: { tab: string; filter: string; page: number; isAdmin: boolean; onPage: (page: number) => void; reload: () => void; onCorrect: (row: Receipt) => void }) {
  const [data, setData] = useState<List<Lot | Receipt | Disposal> | null>(null)
  const [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    const url = tab === 'inventory' ? `/api/stock/inventory?availability=${filter}&page=${page}` : tab === 'receipts' ? `/api/stock/receipts?filter=${filter}&page=${page}` : `/api/stock/disposals?page=${page}`
    const load = () => void stockGet<List<Lot | Receipt | Disposal>>(url, controller.signal).then(result => { setData(result); setError('') }).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    load()
    // Refresh expiry eligibility and the three-month history window while a page stays open.
    const timer = window.setInterval(load, 60000)
    return () => { controller.abort(); window.clearInterval(timer) }
  }, [tab, filter, page])
  if (error) return <p role="alert" className="auth-error">{error}</p>
  if (!data) return <p role="status">Loading stock…</p>
  return <section className="catalogue-panel entry-panel" aria-label={tab === 'inventory' ? 'Stock inventory' : tab === 'disposals' ? 'Disposed stock' : 'Stock entries'}>
    {data.items.length === 0 && <p>No records in this view.</p>}
    {data.items.map(item => tab === 'inventory' ? <InventoryLot key={(item as Lot).lotId} lot={item as Lot} isAdmin={isAdmin} reload={reload} /> : tab === 'receipts' ? <ReceiptCard key={(item as Receipt).id} row={item as Receipt} isAdmin={isAdmin} reload={reload} onCorrect={onCorrect} /> : <DisposalCard key={(item as Disposal).id} row={item as Disposal} />)}
    {(page > 1 || data.hasMore) && <nav className="pagination" aria-label="Stock result pages"><button disabled={page === 1} onClick={() => onPage(page - 1)}>Previous</button><span>Page {page}</span><button disabled={!data.hasMore} onClick={() => onPage(page + 1)}>Next</button></nav>}
  </section>
}
function ReceiptDetails({ row }: { row: Receipt }) {
  return <>
    <h3>{row.brandName} · Batch {row.batchNumber}</h3>
    <p>{row.manufacturer} · Supplier: {row.supplier}{row.deliveryReference ? ` · Ref: ${row.deliveryReference}` : ''}</p>
    <p>Received {row.receivedDate} · Manufactured {row.manufacturingDate ?? 'not recorded'} · Expires {row.expiryDate}</p>
    <p>{row.cartons} cartons + {row.boxes} boxes + {row.strips} strips + {row.pieces} individual {row.baseUnit.toLowerCase()}s = <strong>{row.totalUnits} {row.baseUnit.toLowerCase()}s</strong></p>
    <p className="field-help">{row.unitsPerStrip > 0 ? `${row.unitsPerStrip} per strip · ${row.stripsPerBox} strips per box · ` : ''}{row.unitsPerBox} per box · {row.boxesPerCarton} boxes per carton</p>
    <p>Printed MRP: ৳{row.mrpAmount.toFixed(2)} per {row.mrpUnit === 'Piece' ? row.baseUnit.toLowerCase() : row.mrpUnit.toLowerCase()} ({row.mrpUnits} stock units) · {row.mrpVerified ? 'Verified' : 'Verification pending'}</p>
    <p>Revision {row.revision} · {row.status.replace(/([a-z])([A-Z])/g, '$1 $2')}{row.automaticApproval ? ' automatically' : ''}</p>
    {row.reviewNote && <p>Review note: {row.reviewNote}</p>}{row.correctionReason && <p>Correction reason: {row.correctionReason}</p>}
  </>
}
function ReceiptCard({ row, isAdmin, reload, onCorrect }: { row: Receipt; isAdmin: boolean; reload: () => void; onCorrect: (row: Receipt) => void }) {
  const [note, setNote] = useState(''), [verified, setVerified] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const [history, setHistory] = useState<Receipt[] | null>(null)
  const [correctingMrp, setCorrectingMrp] = useState(false)
  async function act(action: string) {
    setBusy(true); setError('')
    try {
      if (action === 'history') setHistory(await stockGet<Receipt[]>(`/api/stock/receipts/${row.receiptId}`))
      else {
        await stockPost(`/api/stock/receipts/${row.receiptId}/${action === 'verify' ? 'verify-mrp' : 'review'}`, { revisionId: row.id, approve: action === 'approve', verifyMrp: verified, note })
        reload()
      }
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not complete this action.') }
    finally { setBusy(false) }
  }
  return <article className="submission-card">
    <ReceiptDetails row={row} />
    {error && <p role="alert" className="auth-error">{error}</p>}
    <fieldset disabled={busy}>
      <legend className="sr-only">Review stock entry</legend>
      {isAdmin && (row.status === 'PendingApproval' || (row.status === 'Approved' && !row.mrpVerified)) && <p><label><input type="checkbox" checked={verified} onChange={e => setVerified(e.target.checked)} /> I checked this printed MRP against the packaging</label></p>}
      {isAdmin && row.status === 'PendingApproval' && <div className="field"><label htmlFor={`stock-note-${row.id}`}>Review note (required to return)</label><input id={`stock-note-${row.id}`} maxLength={1000} value={note} onChange={e => setNote(e.target.value)} /></div>}
      <div className="entry-actions">
        <button onClick={() => void act('history')}>View entry history</button>
        {isAdmin && row.status === 'Approved' && <button onClick={() => setCorrectingMrp(v => !v)}>Correct MRP</button>}
        {['PendingApproval', 'Returned'].includes(row.status) && <button onClick={() => onCorrect(row)}>Correct entry</button>}
        {isAdmin && row.status === 'PendingApproval' && <><button onClick={() => void act('return')}>Return for correction</button><button className="primary" onClick={() => void act('approve')}>Approve stock</button></>}
        {isAdmin && row.status === 'Approved' && !row.mrpVerified && <button className="primary" disabled={!verified} onClick={() => void act('verify')}>Verify MRP</button>}
      </div>
    </fieldset>
    {correctingMrp && <CorrectMrp row={row} onSaved={reload} onCancel={() => setCorrectingMrp(false)} />}
    {history && <details open><summary>Saved revision history</summary>{history.map(r => <div className="history-entry" key={r.id}><ReceiptDetails row={r} /></div>)}{isAdmin && <MrpHistory receiptId={row.receiptId} />}</details>}
  </article>
}
function InventoryLot({ lot, isAdmin, reload }: { lot: Lot; isAdmin: boolean; reload: () => void }) {
  const [disposing, setDisposing] = useState(false)
  return <article className="submission-card">
    <h3>{lot.brandName} · Batch {lot.batchNumber}</h3><p>{lot.manufacturer} · Expires {lot.expiryDate}</p>
    <p><strong>{lot.onHandUnits} {lot.baseUnit.toLowerCase()}s on hand</strong> · {lot.sellableUnits} sellable</p>
    <p>{packBreakdown(lot.onHandUnits, lot.unitsPerBox, lot.unitsPerStrip, lot.baseUnit)}</p>
    <p className={lot.sellableUnits === 0 ? 'auth-error' : 'save-confirmation'}>{lot.availability}</p>
    <p>{lot.mrpVerified ? 'Verified' : 'Recorded, unverified'} MRP: ৳{lot.mrpAmount.toFixed(2)} per {lot.mrpUnit === 'Piece' ? lot.baseUnit.toLowerCase() : lot.mrpUnit.toLowerCase()}</p>
    {isAdmin && lot.availability.startsWith('Expired') && <button onClick={() => setDisposing(v => !v)}>Record physical disposal</button>}
    {disposing && <DisposeForm lot={lot} reload={reload} />}
  </article>
}
function DisposeForm({ lot, reload }: { lot: Lot; reload: () => void }) {
  const [reason, setReason] = useState(''), [confirmed, setConfirmed] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState('')
  const pending = useRef<{ reason: string; id: string } | null>(null)
  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (busy) return
    setBusy(true); setError('')
    if (pending.current?.reason !== reason.trim()) pending.current = { reason: reason.trim(), id: crypto.randomUUID() }
    try { await stockPost('/api/stock/disposals', { requestId: pending.current.id, lotId: lot.lotId, reason: reason.trim(), physicallyDisposed: confirmed }); reload() }
    catch (e) { setError(e instanceof Error ? e.message : 'Disposal was not confirmed.') }
    finally { setBusy(false) }
  }
  return <form onSubmit={submit}><fieldset disabled={busy}>
    <legend>Remove physically disposed stock</legend>
    <div className="field"><label htmlFor={`disposal-reason-${lot.lotId}`}>Disposal reason or reference</label><input id={`disposal-reason-${lot.lotId}`} required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} /></div>
    <p><label><input type="checkbox" required checked={confirmed} onChange={e => setConfirmed(e.target.checked)} /> I have physically disposed of all {lot.onHandUnits} {lot.baseUnit.toLowerCase()}s in this stock lot</label></p>
    <p className="field-help">This removes the quantity from inventory. Its disposal record can be viewed and exported for three calendar months.</p>
    {error && <p role="alert" className="auth-error">{error}</p>}<button type="submit" className="primary">{busy ? 'Saving…' : 'Confirm disposal'}</button>
  </fieldset></form>
}
function DisposalCard({ row }: { row: Disposal }) {
  return <article className="submission-card"><h3>{row.brandName} · Batch {row.batchNumber}</h3><p>{row.quantity} {row.baseUnit.toLowerCase()}s disposed · Expired {row.expiryDate}</p><p>{row.reason}</p><p>Recorded by {row.disposedBy} on {new Date(row.disposedAt).toLocaleString()}</p><p className="field-help">Available to view and export until {new Date(row.visibleUntil).toLocaleString()}</p></article>
}
