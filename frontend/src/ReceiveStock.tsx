import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { packBreakdown, stockGet, stockPost } from './stockApi'
import type { Receipt, StockMedicine } from './stockApi'

type Saved = { receiptId: string; revisionId: string; status: string; totalUnits: number; mrpVerified: boolean }
export function ReceiveStock({ medicineId, original, isAdmin, onCancel, onSaved }: { medicineId: string; original?: Receipt; isAdmin: boolean; onCancel: () => void; onSaved: (message: string) => void }) {
  const [medicine, setMedicine] = useState<StockMedicine | null>(null)
  const [today, setToday] = useState('')
  const [supplier, setSupplier] = useState(original?.supplier ?? '')
  const [reference, setReference] = useState(original?.deliveryReference ?? '')
  const [receivedDate, setReceivedDate] = useState(original?.receivedDate ?? '')
  const [batchNumber, setBatchNumber] = useState(original?.batchNumber ?? '')
  const [manufacturingDate, setManufacturingDate] = useState(original?.manufacturingDate ?? '')
  const [expiryDate, setExpiryDate] = useState(original?.expiryDate ?? '')
  const [hasStrips, setHasStrips] = useState((original?.unitsPerStrip ?? 0) > 0)
  const [unitsPerStrip, setUnitsPerStrip] = useState(original ? String(original.unitsPerStrip) : '')
  const [stripsPerBox, setStripsPerBox] = useState(original ? String(original.stripsPerBox) : '')
  const [unitsPerBox, setUnitsPerBox] = useState(original ? String(original.unitsPerBox) : '')
  const [boxesPerCarton, setBoxesPerCarton] = useState(original ? String(original.boxesPerCarton) : '')
  const [counts, setCounts] = useState({ pieces: String(original?.pieces ?? 0), strips: String(original?.strips ?? 0), boxes: String(original?.boxes ?? 0), cartons: String(original?.cartons ?? 0) })
  const [mrpAmount, setMrpAmount] = useState(original ? String(original.mrpAmount) : '')
  const [mrpUnit, setMrpUnit] = useState(original?.mrpUnit ?? '')
  const [verifyMrp, setVerifyMrp] = useState(false)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const pending = useRef<{ payload: string; requestId: string } | null>(null)
  const submitting = useRef(false)
  useEffect(() => {
    const controller = new AbortController()
    void stockGet<{ medicine: StockMedicine; today: string }>(`/api/stock/medicines/${medicineId}`, controller.signal).then(data => {
      setMedicine(data.medicine); setToday(data.today)
      if (!original) { setReceivedDate(data.today); setHasStrips(['Tablet', 'Capsule'].includes(data.medicine.baseUnit)) }
    }).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    return () => controller.abort()
  }, [medicineId, original])
  const perStrip = hasStrips ? Number(unitsPerStrip) : 0
  const perBox = hasStrips ? perStrip * Number(stripsPerBox) : Number(unitsPerBox)
  const perCarton = Number(boxesPerCarton || 1)
  const total = Number(counts.pieces) + Number(counts.strips) * perStrip + (Number(counts.boxes) + Number(counts.cartons) * perCarton) * perBox
  const mrpDenominator = mrpUnit === 'Piece' ? 1 : mrpUnit === 'Strip' ? perStrip : perBox
  function selectSavedBatch(id: string) {
    const batch = medicine?.batches.find(b => b.id === id)
    setBatchNumber(batch?.batchNumber ?? ''); setManufacturingDate(batch?.manufacturingDate ?? ''); setExpiryDate(batch?.expiryDate ?? '')
  }
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (submitting.current) return
    if (!receivedDate || !expiryDate || (manufacturingDate && (manufacturingDate > expiryDate || manufacturingDate > receivedDate))) { setError('Check the receipt, manufacturing and expiry dates.'); return }
    if (!Number.isSafeInteger(total) || total < 1 || total > 1000000000) { setError('Enter quantities totaling between 1 and 1,000,000,000 stock units.'); return }
    const details = { medicineId, supplier: supplier.trim(), deliveryReference: reference.trim(), receivedDate, batchNumber: batchNumber.trim(), manufacturingDate: manufacturingDate || null, expiryDate,
      unitsPerStrip: perStrip, stripsPerBox: hasStrips ? Number(stripsPerBox) : 0, unitsPerBox: perBox, boxesPerCarton: perCarton,
      pieces: Number(counts.pieces), strips: hasStrips ? Number(counts.strips) : 0, boxes: Number(counts.boxes), cartons: Number(counts.cartons), mrpAmount: Number(mrpAmount), mrpUnit, verifyMrp,
      receiptId: original?.receiptId ?? null, replacesRevisionId: original?.id ?? null, correctionReason: original ? reason.trim() : null }
    const payload = JSON.stringify(details)
    if (pending.current?.payload !== payload) pending.current = { payload, requestId: crypto.randomUUID() }
    submitting.current = true; setBusy(true); setError('')
    try {
      const saved = await stockPost<Saved>('/api/stock/receipts', { ...details, requestId: pending.current.requestId })
      if (saved.revisionId !== pending.current.requestId) throw new Error('Save was not confirmed. Keep the entry unchanged and retry.')
      onSaved(`${medicine?.brandName}: ${saved.totalUnits} ${medicine?.baseUnit.toLowerCase()}s ${saved.status === 'Approved' ? 'received and approved' : 'submitted for admin approval'}.${expiryDate < today ? ' Expired stock cannot be sold.' : saved.status === 'Approved' && !saved.mrpVerified ? ' MRP verification is still required before sale.' : ''}`)
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not save this entry.') }
    finally { submitting.current = false; setBusy(false) }
  }
  return <section className="catalogue-panel entry-panel" aria-label="Receive stock">
    <h2>{original ? 'Correct stock entry' : 'Receive stock'}{medicine ? ` — ${medicine.brandName}` : ''}</h2>
    {medicine && <p>{medicine.manufacturer} · {medicine.dosageForm}</p>}
    <p className="field-help">{isAdmin ? 'Your stock entry is approved automatically. MRP verification is a separate checkbox.' : 'An admin must review your quantities and printed MRP before this stock can be sold.'}</p>
    {error && <p className="auth-error" role="alert">{error}</p>}
    {!medicine ? <><p>Loading medicine details…</p><button onClick={onCancel}>Back</button></> : <form onSubmit={submit}>
      <fieldset disabled={busy}>
        <legend className="sr-only">Stock receiving details</legend>
        <h3>Delivery and batch</h3>
        <div className="entry-grid">
          <div className="field"><label htmlFor="stock-supplier">Supplier</label><input id="stock-supplier" required maxLength={200} value={supplier} onChange={e => setSupplier(e.target.value)} /></div>
          <div className="field"><label htmlFor="stock-reference">Delivery reference (optional)</label><input id="stock-reference" maxLength={100} value={reference} onChange={e => setReference(e.target.value)} /></div>
          <div className="field"><label htmlFor="received-date">Received date</label><input id="received-date" type="date" required max={today} value={receivedDate} onInput={e => setReceivedDate(e.currentTarget.value)} /></div>
          {!original && medicine.batches.length > 0 && <div className="field"><label htmlFor="saved-batch">Copy a saved batch</label><select id="saved-batch" defaultValue="" onChange={e => selectSavedBatch(e.target.value)}><option value="">New batch / enter manually</option>{medicine.batches.map(b => <option key={b.id} value={b.id}>{b.batchNumber} — expires {b.expiryDate}</option>)}</select></div>}
          <div className="field"><label htmlFor="stock-batch">Batch number</label><input id="stock-batch" required maxLength={100} value={batchNumber} onChange={e => setBatchNumber(e.target.value)} /></div>
          <div className="field"><label htmlFor="stock-manufactured">Manufacturing date (if shown)</label><input id="stock-manufactured" type="date" value={manufacturingDate} onInput={e => setManufacturingDate(e.currentTarget.value)} /></div>
          <div className="field"><label htmlFor="stock-expiry">Expiry date</label><input id="stock-expiry" type="date" required value={expiryDate} onInput={e => setExpiryDate(e.currentTarget.value)} /></div>
        </div>
        {expiryDate && expiryDate < today && <p className="auth-error">This batch is expired. After approval it will appear as unsellable stock.</p>}
        <h3>Packaging on this delivery</h3>
        <p className="field-help">Copy pack sizes from the label. These sizes are saved with this entry.</p>
        {['Tablet', 'Capsule'].includes(medicine.baseUnit) && <p><label><input type="checkbox" checked={hasStrips} onChange={e => { setHasStrips(e.target.checked); setCounts({ ...counts, strips: '0' }); setMrpUnit('') }} /> This medicine is packed in strips</label></p>}
        <div className="entry-grid">
          {hasStrips ? <><NumberField id="units-strip" label={`${medicine.baseUnit}s per strip`} value={unitsPerStrip} set={setUnitsPerStrip} min={1} /><NumberField id="strips-box" label="Strips per box" value={stripsPerBox} set={setStripsPerBox} min={1} /></> : <NumberField id="units-box" label={`${medicine.baseUnit}s per box`} value={unitsPerBox} set={setUnitsPerBox} min={1} />}
          <NumberField id="boxes-carton" label="Boxes per carton (required for cartons)" value={boxesPerCarton} set={setBoxesPerCarton} min={1} required={Number(counts.cartons) > 0} />
        </div>
        <h3>Quantity received</h3>
        <p className="field-help">Enter separate quantities below. For example, 1 box plus 2 loose strips plus 3 individual tablets. Do not count the tablets inside that box again.</p>
        <div className="entry-grid">
          {(['pieces', ...(hasStrips ? ['strips'] : []), 'boxes', 'cartons'] as const).map(key => <NumberField key={key} id={`count-${key}`} label={key === 'pieces' ? `Individual ${medicine.baseUnit.toLowerCase()}s` : key[0].toUpperCase() + key.slice(1)} value={counts[key as keyof typeof counts]} set={value => setCounts({ ...counts, [key]: value })} min={0} />)}
        </div>
        <p className="quantity-summary" aria-live="polite">Total: <strong>{Number.isSafeInteger(total) ? total.toLocaleString() : '—'} {medicine.baseUnit.toLowerCase()}s</strong>{perBox > 0 && <span className="secondary">Equivalent to {packBreakdown(total, perBox, perStrip, medicine.baseUnit)}</span>}</p>
        <h3>Printed MRP</h3>
        <div className="entry-grid">
          <div className="field"><label htmlFor="printed-mrp">MRP amount (৳)</label><input id="printed-mrp" type="number" min="0.01" max="1000000000" step="0.01" required value={mrpAmount} onChange={e => setMrpAmount(e.target.value)} /></div>
          <div className="field"><label htmlFor="mrp-unit">MRP is for</label><select id="mrp-unit" required value={mrpUnit} onChange={e => setMrpUnit(e.target.value)}><option value="">Select the unit printed beside the MRP</option><option value="Piece">One {medicine.baseUnit.toLowerCase()}</option>{hasStrips && <option value="Strip">One strip</option>}<option value="Box">One box</option></select></div>
        </div>
        {mrpUnit && mrpDenominator > 0 && Number(mrpAmount) > 0 && <p className="field-help">৳{Number(mrpAmount).toFixed(2)} ÷ {mrpDenominator} {medicine.baseUnit.toLowerCase()}s. Estimated value of this delivery at MRP: ৳{(Number(mrpAmount) * total / mrpDenominator).toFixed(2)}. This is not the purchase cost.</p>}
        {isAdmin && <p><label><input type="checkbox" checked={verifyMrp} onChange={e => setVerifyMrp(e.target.checked)} /> I checked this printed MRP against the packaging</label></p>}
        {original && <div className="field"><label htmlFor="correction-reason">Reason for correction</label><input id="correction-reason" required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} /></div>}
        <div className="entry-actions"><button type="button" onClick={onCancel}>Cancel</button><button type="submit" className="primary">{busy ? 'Saving…' : isAdmin ? 'Save stock entry' : 'Submit stock for approval'}</button></div>
      </fieldset>
    </form>}
  </section>
}
function NumberField({ id, label, value, set, min, required = true }: { id: string; label: string; value: string; set: (value: string) => void; min: number; required?: boolean }) {
  return <div className="field"><label htmlFor={id}>{label}</label><input id={id} type="number" min={min} max="1000000" step="1" required={required} value={value} onChange={e => set(e.target.value)} /></div>
}
