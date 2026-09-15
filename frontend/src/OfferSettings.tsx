import { useEffect, useRef, useState } from 'react'
import { MedicinePicker } from './ChargeSettings'
import { stockGet, stockPost } from './stockApi'

type Medicine = { id: string; brandName: string; manufacturer: string; dosageForm: string }
type Offer = { id: string; name: string; medicineName: string | null; kind: string; value: number; unit: string; units: number; minimumSubtotal: number; startDate: string; endDate: string | null; stoppedAt: string | null; status: string }
export function OfferSettings({ onBack }: { onBack: () => void }) {
  const [offers, setOffers] = useState<Offer[] | null>(null), [refresh, setRefresh] = useState(0)
  const [name, setName] = useState(''), [scope, setScope] = useState('medicine'), [medicine, setMedicine] = useState<Medicine | null>(null)
  const [kind, setKind] = useState('Percentage'), [value, setValue] = useState(''), [unit, setUnit] = useState('Piece'), [units, setUnits] = useState('1')
  const [start, setStart] = useState(''), [end, setEnd] = useState(''), [minimum, setMinimum] = useState('0'), [search, setSearch] = useState('')
  const [error, setError] = useState(''), [message, setMessage] = useState(''), [busy, setBusy] = useState(false), [stopping, setStopping] = useState<string | null>(null)
  const retry = useRef<{ key: string; id: string } | null>(null)
  const fixedMedicine = scope === 'medicine' && kind === 'Fixed'
  useEffect(() => {
    const controller = new AbortController()
    void stockGet<Offer[]>('/api/offers', controller.signal).then(setOffers).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    return () => controller.abort()
  }, [refresh])
  return <>
    <div className="page-heading"><div><p className="eyebrow">ADMIN SETTINGS</p><h1>Discount offers</h1><p className="subtitle">Automatic offers for medicines or the whole bill.</p></div><button onClick={onBack}>Medicine catalogue</button></div>
    <p className="notice">The cart chooses the best eligible medicine offers or one whole-sale offer. They never stack. Charges are added after discounts.</p>
    {error && <p className="auth-error" role="alert">{error}</p>}{message && <p role="status" className="save-confirmation">{message}</p>}
    <form className="catalogue-panel charge-form" onSubmit={async e => {
      e.preventDefault(); setError(''); setMessage('')
      if (scope === 'medicine' && !medicine) { setError('Select a medicine for this offer.'); return }
      const payload = { name, medicineId: scope === 'medicine' ? medicine!.id : null, kind, value: Number(value), unit: fixedMedicine ? unit : 'Piece', units: fixedMedicine && unit !== 'Piece' ? Number(units) : 1, startDate: start, endDate: end || null, minimumSubtotal: scope === 'sale' ? Number(minimum) : 0 }
      const key = JSON.stringify(payload)
      if (retry.current?.key !== key) retry.current = { key, id: crypto.randomUUID() }
      setBusy(true)
      try { await stockPost('/api/offers', { ...payload, requestId: retry.current.id }); retry.current = null; setName(''); setValue(''); setMessage('Offer saved. Eligible carts will apply it automatically when it gives the best price.'); setRefresh(v => v + 1) }
      catch (e) { setError((e as Error).message) } finally { setBusy(false) }
    }}>
      <h2>Add an offer</h2><fieldset disabled={busy} className="charge-fields">
        <div className="field"><label htmlFor="offer-name">Offer name</label><input id="offer-name" required maxLength={100} value={name} onChange={e => setName(e.target.value)} /></div>
        <div className="field"><label htmlFor="offer-scope">Offer applies to</label><select id="offer-scope" value={scope} onChange={e => setScope(e.target.value)}><option value="medicine">One medicine</option><option value="sale">Whole sale</option></select></div>
        {scope === 'medicine' && <div>{medicine && <p>Selected: <strong>{medicine.brandName}</strong> · {medicine.manufacturer}</p>}<div className="field"><label htmlFor="offer-search">Find medicine for offer</label><input id="offer-search" type="search" maxLength={100} value={search} onChange={e => setSearch(e.target.value)} /></div><MedicinePicker key={search} search={search} selected={medicine ? [medicine] : []} onAdd={m => { setMedicine(m); setUnit('Piece'); setUnits('1') }} /></div>}
        <div className="field"><label htmlFor="offer-kind">Discount calculation</label><select id="offer-kind" value={kind} onChange={e => setKind(e.target.value)}><option value="Percentage">Percentage (%)</option><option value="Fixed">Fixed amount (৳)</option></select></div>
        <div className="field"><label htmlFor="offer-value">{kind === 'Percentage' ? 'Discount percentage' : 'Discount amount in taka'}</label><input id="offer-value" required type="number" min="0.01" max={kind === 'Percentage' ? 100 : 1000000} step="0.01" value={value} onChange={e => setValue(e.target.value)} /></div>
        {fixedMedicine && <><div className="field"><label htmlFor="offer-unit">Discount per</label><select id="offer-unit" value={unit} onChange={e => { setUnit(e.target.value); setUnits('') }}><option value="Piece">Individual piece</option><option value="Strip">Strip</option><option value="Box">Box</option></select></div>{unit !== 'Piece' && <div className="field"><label htmlFor="offer-units">Individual stock units per {unit.toLowerCase()}</label><input id="offer-units" type="number" min="1" max="1000000" step="1" required value={units} onChange={e => setUnits(e.target.value)} /><p className="field-help">Read this from the medicine packaging. This offer keeps the saved conversion and prorates smaller quantities.</p></div>}</>}
        {scope === 'sale' && <div className="field"><label htmlFor="offer-minimum">Minimum MRP bill amount (৳)</label><input id="offer-minimum" required type="number" min="0" max="1000000000" step="0.01" value={minimum} onChange={e => setMinimum(e.target.value)} /><p className="field-help">Before discounts and charges. Zero means every sale during the offer dates.</p></div>}
        <div className="field"><label htmlFor="offer-start">Start date</label><input id="offer-start" type="date" required value={start} onInput={e => setStart(e.currentTarget.value)} /></div>
        <div className="field"><label htmlFor="offer-end">End date (optional)</label><input id="offer-end" type="date" min={start || undefined} value={end} onInput={e => setEnd(e.currentTarget.value)} /><p className="field-help">Dates use Bangladesh time and include the full end date. Leave blank to continue until stopped.</p></div>
        <p className="field-help">Discounts are capped at the medicine price. The final bill is rounded once after charges.</p>
        <button type="submit" className="primary">{busy ? 'Saving…' : 'Save offer'}</button>
      </fieldset>
    </form>
    <section className="catalogue-panel charge-form" aria-label="Saved offers"><div className="cart-heading"><h2>Saved offers</h2><button onClick={() => { setError(''); setRefresh(v => v + 1) }}>Refresh offers</button></div><p>To change an offer, stop it and add a replacement. Previous versions remain here.</p>
      {!offers ? <p>Loading offers…</p> : offers.length === 0 ? <p>No offers configured.</p> : offers.map(o => <article className="counter-product" key={o.id}><h3>{o.name} · {o.status}</h3><p>{o.medicineName || 'Whole sale'} · {o.kind === 'Percentage' ? `${o.value}% off` : `৳${o.value} off${o.medicineName ? ` per ${o.unit.toLowerCase()} (${o.units} stock units)` : ''}`}</p><p>Starts {o.startDate} · {o.endDate ? `Ends ${o.endDate}` : 'No end date'}</p>{!o.medicineName && <p>Minimum MRP bill: ৳{o.minimumSubtotal}</p>}
        {!o.stoppedAt && (stopping === o.id ? <div><p>Stop this offer for new cart calculations?</p><button disabled={busy} onClick={async () => { setBusy(true); setError(''); try { await stockPost(`/api/offers/${o.id}/stop`, {}); setStopping(null); setMessage(`${o.name} stopped.`); setRefresh(v => v + 1) } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}>Confirm stop</button> <button disabled={busy} onClick={() => setStopping(null)}>Cancel</button></div> : <button disabled={busy} onClick={() => setStopping(o.id)}>Stop {o.name}</button>)}
      </article>)}
    </section>
  </>
}
