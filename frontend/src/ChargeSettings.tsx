import { useEffect, useRef, useState } from 'react'
import { stockGet, stockPost } from './stockApi'

type Medicine = { id: string; brandName: string; manufacturer: string; dosageForm: string }
type Rule = { id: string; name: string; kind: string; value: number; allMedicines: boolean; stoppedAt: string | null; medicines: { id: string; name: string }[] }
const kinds: Record<string, string> = { Percentage: 'Percentage (%)', PerUnit: 'Fixed amount per piece (৳)', PerMedicine: 'Fixed amount once per medicine (৳)' }
export function ChargeSettings({ onBack }: { onBack: () => void }) {
  const [rules, setRules] = useState<Rule[]>([]), [refresh, setRefresh] = useState(0), [loading, setLoading] = useState(true)
  const [error, setError] = useState(''), [message, setMessage] = useState(''), [busy, setBusy] = useState(false)
  const [name, setName] = useState(''), [kind, setKind] = useState('Percentage'), [value, setValue] = useState(''), [scope, setScope] = useState('all')
  const [selected, setSelected] = useState<Medicine[]>([]), [search, setSearch] = useState('')
  const [stopping, setStopping] = useState<string | null>(null)
  const retry = useRef<{ key: string; id: string } | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    void stockGet<Rule[]>('/api/charges', controller.signal).then(data => { setRules(data); setLoading(false) }).catch(e => { if (!controller.signal.aborted) { setError(e.message); setLoading(false) } })
    return () => controller.abort()
  }, [refresh])
  return <>
    <div className="page-heading"><div><p className="eyebrow">ADMIN SETTINGS</p><h1>Cart charges</h1><p className="subtitle">Set charges that appear when medicines are added to a sale.</p></div><button onClick={onBack}>Medicine catalogue</button></div>
    <p className="notice">Charges start when saved and run until stopped. Percentage charges use the medicine amount after discounts and do not compound with other charges.</p>
    {error && <p className="auth-error" role="alert">{error}</p>}{message && <p className="save-confirmation" role="status">{message}</p>}
    <form className="catalogue-panel charge-form" onSubmit={async e => {
      e.preventDefault(); setError(''); setMessage('')
      if (scope === 'selected' && selected.length === 0) { setError('Select at least one medicine.'); return }
      const payload = { name, kind, value: Number(value), allMedicines: scope === 'all', medicineIds: scope === 'all' ? [] : selected.map(m => m.id) }
      const key = JSON.stringify(payload)
      if (retry.current?.key !== key) retry.current = { key, id: crypto.randomUUID() }
      setBusy(true)
      try { await stockPost('/api/charges', { ...payload, requestId: retry.current.id }); retry.current = null; setName(''); setValue(''); setSelected([]); setMessage('Charge saved. New cart calculations will include it.'); setRefresh(v => v + 1) }
      catch (e) { setError((e as Error).message) } finally { setBusy(false) }
    }}>
      <h2>Add a charge</h2>
      <fieldset disabled={busy} className="charge-fields">
        <div className="field"><label htmlFor="charge-name">Charge name (shown in cart)</label><input id="charge-name" value={name} onChange={e => setName(e.target.value)} required maxLength={100} placeholder="e.g. VAT" /></div>
        <div className="field"><label htmlFor="charge-kind">Calculation</label><select id="charge-kind" value={kind} onChange={e => setKind(e.target.value)}>{Object.entries(kinds).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></div>
        <div className="field"><label htmlFor="charge-value">{kind === 'Percentage' ? 'Percentage' : 'Amount in taka'}</label><input id="charge-value" type="number" min="0.01" max={kind === 'Percentage' ? 100 : 1000000} step="0.01" required value={value} onChange={e => setValue(e.target.value)} /></div>
        <div className="field"><label htmlFor="charge-scope">Applies to</label><select id="charge-scope" value={scope} onChange={e => setScope(e.target.value)}><option value="all">All medicines</option><option value="selected">Selected medicines</option></select></div>
        {kind === 'PerUnit' && <p className="field-help">Per tablet, capsule, bottle or other individual stock unit. Strips and boxes are converted to individual units.</p>}
        {kind === 'PerMedicine' && <p className="field-help">Counted once for each medicine, even across multiple batches or pack sizes. Shared across its cart rows in proportion to their individual quantities.</p>}
        {scope === 'selected' && <div><label htmlFor="charge-medicine-search">Find medicines</label><input id="charge-medicine-search" type="search" value={search} maxLength={100} onChange={e => setSearch(e.target.value)} />
          <p>{selected.length} selected</p>{selected.map(m => <p key={m.id}>{m.brandName} · {m.manufacturer} <button type="button" onClick={() => setSelected(rows => rows.filter(x => x.id !== m.id))}>Remove {m.brandName}</button></p>)}
          <MedicinePicker key={search} search={search} selected={selected} onAdd={m => setSelected(rows => [...rows, m])} />
        </div>}
        <button type="submit" className="primary">{busy ? 'Saving…' : 'Save and activate charge'}</button>
      </fieldset>
    </form>
    <section className="catalogue-panel charge-form" aria-label="Saved charges"><div className="cart-heading"><h2>Saved charges</h2><button onClick={() => { setError(''); setRefresh(v => v + 1) }}>Refresh charges</button></div>
      <p className="field-help">To change a charge, stop it and add its replacement. Stopped rules remain here for reference.</p>
      {loading ? <p role="status">Loading charges…</p> : rules.length === 0 ? <p>No charges configured.</p> : rules.map(rule => <article className="counter-product" key={rule.id}>
        <h3>{rule.name} · {rule.stoppedAt ? 'Stopped' : 'Active'}</h3><p>{kinds[rule.kind]}: {rule.value} · {rule.allMedicines ? 'All medicines' : rule.medicines.map(m => m.name).join(', ')}</p>
        {!rule.stoppedAt && (stopping === rule.id ? <div><p>Stop {rule.name} for new cart calculations?</p><button disabled={busy} onClick={async () => { setBusy(true); setError(''); try { await stockPost(`/api/charges/${rule.id}/stop`, {}); setStopping(null); setMessage(`${rule.name} stopped.`); setRefresh(v => v + 1) } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}>Confirm stop</button> <button disabled={busy} onClick={() => setStopping(null)}>Cancel</button></div> : <button disabled={busy} onClick={() => setStopping(rule.id)}>Stop {rule.name}</button>)}
      </article>)}
    </section>
  </>
}
export function MedicinePicker({ search, selected, onAdd }: { search: string; selected: Medicine[]; onAdd: (m: Medicine) => void }) {
  const [page, setPage] = useState(1), [data, setData] = useState<{ items: Medicine[]; hasMore: boolean } | null>(null), [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => void stockGet<{ items: Medicine[]; hasMore: boolean }>(`/api/medicines?${new URLSearchParams({ search, page: String(page) })}`, controller.signal).then(setData).catch(e => { if (!controller.signal.aborted) setError(e.message) }), 200)
    return () => { controller.abort(); window.clearTimeout(timer) }
  }, [search, page])
  return <div>{error ? <p role="alert">{error}</p> : !data ? <p>Finding medicines…</p> : <>
    {data.items.map(m => <p key={m.id}>{m.brandName} · {m.dosageForm} · {m.manufacturer} <button type="button" disabled={selected.some(x => x.id === m.id) || selected.length >= 500} onClick={() => onAdd(m)}>{selected.some(x => x.id === m.id) ? 'Selected' : `Select ${m.brandName}`}</button></p>)}
    {data.items.length === 0 && <p>No matching medicines.</p>}
    {(page > 1 || data.hasMore) && <div className="pagination"><button type="button" disabled={page === 1} onClick={() => { setData(null); setPage(p => p - 1) }}>Previous</button><span>Page {page}</span><button type="button" disabled={!data.hasMore} onClick={() => { setData(null); setPage(p => p + 1) }}>Next</button></div>}
  </>}</div>
}
