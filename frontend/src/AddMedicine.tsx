import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'

function NameInput({ id, label, kind, value, onChange }: {
  id: string; label: string; kind: 'manufacturer' | 'ingredient'; value: string; onChange: (value: string) => void
}) {
  const [options, setOptions] = useState<string[]>([])
  useEffect(() => {
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ kind, search: value })
      void fetch(`/api/catalogue/lookups?${query}`, { signal: controller.signal, cache: 'no-store' })
        .then(response => response.ok ? response.json() as Promise<string[]> : [])
        .then(names => { if (!controller.signal.aborted) setOptions(names) })
        .catch(() => { if (!controller.signal.aborted) setOptions([]) })
    }, 200)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [kind, value])
  return <div className="field">
    <label htmlFor={id}>{label}</label>
    <input id={id} list={`${id}-options`} required maxLength={200} value={value} onChange={event => onChange(event.target.value)} />
    <datalist id={`${id}-options`}>{options.map(name => <option key={name} value={name} />)}</datalist>
  </div>
}

type Ingredient = { key: string; name: string; strengthValue: string; strengthUnit: string }
const emptyIngredient = (): Ingredient => ({ key: crypto.randomUUID(), name: '', strengthValue: '', strengthUnit: 'mg' })

export function AddMedicine({ isAdmin, onCancel, onSaved }: { isAdmin: boolean; onCancel: () => void; onSaved: (brand: string, status: string) => void }) {
  const [error, setError] = useState('')
  const [brand, setBrand] = useState('')
  const [manufacturer, setManufacturer] = useState('')
  const [makerChoice, setMakerChoice] = useState('')
  const [dosageForm, setDosageForm] = useState('Tablet')
  const [baseUnit, setBaseUnit] = useState('Tablet')
  const [options, setOptions] = useState<{ manufacturers: string[]; forms: string[]; units: string[] } | null>(null)
  const [includeBatch, setIncludeBatch] = useState(false)
  const [batchNumber, setBatchNumber] = useState('')
  const [manufacturingDate, setManufacturingDate] = useState('')
  const [expiryDate, setExpiryDate] = useState('')
  const [addAnother, setAddAnother] = useState(true)
  const [confirmation, setConfirmation] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    void fetch('/api/catalogue/options', { signal: controller.signal }).then(r => {
      if (!r.ok) throw new Error('Could not load form options. Close and reopen the form to retry.')
      return r.json()
    }).then(setOptions).catch(e => { if (!controller.signal.aborted) setError(String(e.message)) })
    return () => controller.abort()
  }, [])
  const [classification, setClassification] = useState('')
  const [ingredients, setIngredients] = useState<Ingredient[]>(() => [emptyIngredient()])
  const [busy, setBusy] = useState(false)
  const pending = useRef<{ payload: string; requestId: string } | null>(null)
  const submitting = useRef(false)
  const title = useRef<HTMLHeadingElement>(null)
  useEffect(() => { title.current?.focus() }, [])

  function updateIngredient(key: string, update: Partial<Ingredient>) {
    setIngredients(rows => rows.map(row => row.key === key ? { ...row, ...update } : row))
  }
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (includeBatch && (!expiryDate || (manufacturingDate && manufacturingDate > expiryDate))) {
      setError('Enter the expiry date. Manufacturing must not be after expiry.'); return
    }
    if (submitting.current) return
    submitting.current = true
    setBusy(true)
    setError('')
    const details = {
      brandName: brand.trim(), manufacturer: manufacturer.trim(), classification, dosageForm, baseUnit,
      firstBatch: includeBatch ? { batchNumber: batchNumber.trim(), manufacturingDate: manufacturingDate || null, expiryDate } : null,
      ingredients: ingredients.map(row => ({ name: row.name.trim(), strengthValue: Number(row.strengthValue), strengthUnit: row.strengthUnit })),
    }
    const payload = JSON.stringify(details)
    if (pending.current?.payload !== payload) pending.current = { payload, requestId: crypto.randomUUID() }
    try {
      const sessionResponse = await fetch('/api/auth/session', { cache: 'no-store', signal: AbortSignal.timeout(10000) })
      if (!sessionResponse.ok) throw new Error('Could not check sign-in. Your entries are still here; try again.')
      const session = await sessionResponse.json() as { csrfToken: string; user: { roles: string[] } | null }
      if (!session.user?.roles.some(role => ['Admin', 'Operator', 'Manager'].includes(role))) throw new Error('Sign in as staff to submit medicines. Your entries have not been submitted.')
      const response = await fetch('/api/medicines', {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken },
        body: JSON.stringify({ requestId: pending.current.requestId, ...details }), signal: AbortSignal.timeout(15000),
      })
      if (!response.ok) {
        const failure = await response.json().catch(() => ({})) as { message?: string }
        throw new Error(failure.message ?? 'Save was not confirmed. Keep the form unchanged and retry.')
      }
      const saved = await response.json() as { id?: string; brandName?: string; reviewStatus?: string }
      if (saved.id !== pending.current.requestId || !saved.brandName) throw new Error('Save was not confirmed. Keep the form unchanged and retry.')
      if (addAnother) {
        setConfirmation(`${saved.brandName}: ${saved.reviewStatus === 'Approved' ? 'saved and approved' : 'submitted for admin approval'}. Ready for the next medicine.`)
        setBrand(''); setIngredients([emptyIngredient()]); setBatchNumber(''); setManufacturingDate(''); setExpiryDate('')
        pending.current = null
        window.setTimeout(() => document.getElementById('brand-name')?.focus(), 0)
      } else onSaved(saved.brandName, saved.reviewStatus ?? 'PendingReview')
    } catch (failure) {
      setError(failure instanceof Error && failure.name !== 'TimeoutError'
        ? failure.message : 'Save was not confirmed. Keep the form unchanged and retry safely.')
    } finally { submitting.current = false; setBusy(false) }
  }

  return <section className="catalogue-panel entry-panel" aria-labelledby="add-heading">
    <h2 id="add-heading" ref={title} tabIndex={-1}>Add medicine</h2>
    <p className="subtitle">Enter catalogue details. This does not add stock or set a price.</p>
    <p className="field-help">{isAdmin ? 'Your entries are approved automatically.' : 'Your entries need admin approval before they appear in the catalogue.'}</p>
    {confirmation && <p role="status" className="save-confirmation">{confirmation}</p>}
    <form onSubmit={submit}>
      <fieldset disabled={busy}>
        <legend className="sr-only">Medicine details</legend>
        <div className="entry-grid">
          <div className="field"><label htmlFor="brand-name">Brand name</label><input id="brand-name" required maxLength={200} placeholder="e.g. Jardimet" value={brand} onChange={event => setBrand(event.target.value)} /></div>
          <div className="field"><label htmlFor="manufacturer">Manufacturer</label>
            <select id="manufacturer" required value={makerChoice} onChange={e => { setMakerChoice(e.target.value); setManufacturer(e.target.value === 'Other' ? '' : e.target.value) }}>
              <option value="">{options ? 'Select manufacturer' : 'Loading manufacturers…'}</option>
              {options?.manufacturers.map(name => <option key={name}>{name}</option>)}<option value="Other">Other — enter name</option>
            </select>
            {makerChoice === 'Other' && <><label htmlFor="other-manufacturer">Other manufacturer name</label><input id="other-manufacturer" required maxLength={200} value={manufacturer} onChange={e => setManufacturer(e.target.value)} /></>}
          </div>
          <div className="field"><label htmlFor="dosage-form">Dosage form</label><select id="dosage-form" required value={dosageForm} onChange={e => {
            const form = e.target.value; setDosageForm(form)
            setBaseUnit(form === 'Tablet' || form === 'Capsule' ? form : ['Syrup', 'Suspension', 'Solution', 'Drops', 'Lotion'].includes(form) ? 'Bottle' : ['Cream', 'Ointment', 'Gel'].includes(form) ? 'Tube' : 'Piece')
          }}>{options?.forms.map(form => <option key={form}>{form}</option>)}</select></div>
          <div className="field"><label htmlFor="base-unit">Smallest stock unit</label><select id="base-unit" required value={baseUnit} disabled={['Tablet', 'Capsule'].includes(dosageForm)} onChange={e => setBaseUnit(e.target.value)}>{options?.units.map(unit => <option key={unit}>{unit}</option>)}</select><small>For example, one bottle of syrup or one vial of injection.</small></div>
          <div className="field"><label htmlFor="classification">Classification</label><select id="classification" required value={classification} onChange={event => setClassification(event.target.value)}><option value="">Select classification</option><option value="Prescription">Prescription</option><option value="Otc">OTC</option></select></div>
        </div>
        <h3>Ingredients and strength</h3>
        <p className="field-help">Enter each ingredient separately. Choose an existing name from the suggestions, or type a new one.</p>
        {ingredients.map((row, index) => <div className="ingredient-entry" key={row.key}>
          <NameInput id={`ingredient-${row.key}`} label={`Ingredient ${index + 1}`} kind="ingredient" value={row.name} onChange={name => updateIngredient(row.key, { name })} />
          <div className="field"><label htmlFor={`strength-${row.key}`}>Strength {index + 1}</label><input id={`strength-${row.key}`} type="number" required min="0.000001" max="999999999.999999" step="0.000001" value={row.strengthValue} onChange={event => updateIngredient(row.key, { strengthValue: event.target.value })} /></div>
          <div className="field"><label htmlFor={`unit-${row.key}`}>Unit {index + 1}</label><select id={`unit-${row.key}`} value={row.strengthUnit} onChange={event => updateIngredient(row.key, { strengthUnit: event.target.value })}>{['mg', 'g', 'mcg', 'IU', 'mg/mL', 'mg/5mL', 'mcg/mL', 'IU/mL', '% w/w', '% w/v', 'mg/dose', 'mcg/dose'].map(unit => <option key={unit}>{unit}</option>)}</select></div>
          <button type="button" disabled={ingredients.length === 1} aria-label={`Remove ingredient ${index + 1}`} onClick={() => setIngredients(rows => rows.filter(item => item.key !== row.key))}>Remove</button>
        </div>)}
        <button type="button" disabled={ingredients.length >= 20} onClick={() => setIngredients(rows => [...rows, emptyIngredient()])}>+ Add ingredient</button>
        <h3>First batch (optional)</h3>
        <label><input type="checkbox" checked={includeBatch} onChange={e => setIncludeBatch(e.target.checked)} /> Record batch number and dates</label>
        <p className="field-help">Copy the dates from the packaging. These identify this batch; they do not add stock for sale.</p>
        {includeBatch && <div className="entry-grid">
          <div className="field"><label htmlFor="batch-number">Batch number</label><input id="batch-number" required maxLength={100} value={batchNumber} onChange={e => setBatchNumber(e.target.value)} /></div>
          <div className="field"><label htmlFor="manufacturing-date">Manufacturing date (if shown)</label><input id="manufacturing-date" type="date" max={expiryDate || undefined} value={manufacturingDate} onInput={e => setManufacturingDate(e.currentTarget.value)} /></div>
          <div className="field"><label htmlFor="expiry-date">Expiry date</label><input id="expiry-date" type="date" required min={manufacturingDate || undefined} value={expiryDate} onInput={e => setExpiryDate(e.currentTarget.value)} /></div>
        </div>}
        <p><label><input type="checkbox" checked={addAnother} onChange={e => setAddAnother(e.target.checked)} /> Keep the form open to add another medicine</label></p>
        {error && <p className="auth-error" role="alert">{error}</p>}
        <div className="entry-actions"><button type="button" onClick={onCancel}>Cancel</button><button type="submit" className="primary" disabled={!options}>{busy ? 'Saving…' : isAdmin ? 'Save medicine' : 'Submit for approval'}</button></div>
      </fieldset>
    </form>
  </section>
}
