import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'
import { StaffManagement } from './StaffManagement'
import { StaffSession } from './StaffSession'
import { AddMedicine } from './AddMedicine'
import { CatalogueSubmissions } from './CatalogueSubmissions'
import { StockWorkspace } from './StockWorkspace'
import { OfferSettings } from './OfferSettings'
import { ChargeSettings } from './ChargeSettings'
import { SalesCounter } from './SalesCounter'

type Medicine = {
  id: string
  brandName: string
  manufacturer: string
  dosageForm: string
  classification: 'Otc' | 'Prescription'
  ingredients: { name: string; strengthValue: number; strengthUnit: string }[]
}
type Catalogue = { items: Medicine[]; page: number; hasMore: boolean }

function CatalogueResults({ search, page, onPage, onReceive }: {
  search: string; page: number; onPage: (page: number) => void; onReceive: (medicineId: string) => void
}) {
  const [result, setResult] = useState<Catalogue | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    const timeout = window.setTimeout(() => controller.abort(), 10000)
    let active = true
    async function load() {
      try {
        const params = new URLSearchParams({ search, page: String(page) })
        const response = await fetch(`/api/medicines?${params}`, {
          signal: controller.signal, cache: 'no-store',
        })
        if (response.status === 401) window.dispatchEvent(new Event('session-expired'))
        if (!response.ok) throw new Error('Catalogue request failed')
        const data = await response.json() as Catalogue
        if (!Array.isArray(data.items) || data.page !== page || typeof data.hasMore !== 'boolean') {
          throw new Error('Unexpected catalogue response')
        }
        if (active) setResult(data)
      } catch {
        if (active) setFailed(true)
      } finally {
        window.clearTimeout(timeout)
      }
    }
    void load()
    return () => {
      active = false
      controller.abort()
      window.clearTimeout(timeout)
    }
  }, [search, page])

  if (failed) return <div className="notice error" role="alert">
    <h2>Couldn’t load the catalogue</h2>
    <p>Check that the backend and database are running, then select Refresh.</p>
  </div>
  if (!result) return <div className="notice" role="status">Loading medicines…</div>
  return <>
    <p className="result-summary" role="status">{result.items.length} {result.items.length === 1 ? 'medicine' : 'medicines'} on this page</p>
    {result.items.length === 0 ? <div className="notice empty">
      <span className="empty-symbol" aria-hidden="true">＋</span>
      <h2>{search ? 'No matching medicines' : page > 1 ? 'No more medicines' : 'No medicines yet'}</h2>
      <p>{search ? 'Try another brand, ingredient, or manufacturer.' : 'Medicine records will appear here once they have been added.'}</p>
    </div> : <div className="table-scroll" tabIndex={0} role="region" aria-label="Medicine catalogue table">
      <table>
        <caption className="sr-only">Active medicines and their catalogue details</caption>
        <thead><tr><th scope="col">Medicine</th><th scope="col">Ingredients & strength</th><th scope="col">Manufacturer</th><th scope="col">Classification</th></tr></thead>
        <tbody>{result.items.map(medicine => <tr key={medicine.id}>
          <td><strong>{medicine.brandName}</strong><span className="secondary">{medicine.dosageForm}</span><button className="receive-button" onClick={() => onReceive(medicine.id)}>Receive stock</button></td>
          <td>{medicine.ingredients.length ? medicine.ingredients.map((ingredient, index) => <span className="ingredient" key={index}>{ingredient.name} <span className="strength">{ingredient.strengthValue} {ingredient.strengthUnit}</span></span>) : 'Not recorded'}</td>
          <td>{medicine.manufacturer}</td>
          <td><span className={`badge ${medicine.classification === 'Prescription' ? 'prescription' : 'otc'}`}>{medicine.classification === 'Prescription' ? 'Prescription' : 'OTC'}</span></td>
        </tr>)}</tbody>
      </table>
    </div>}
    {(page > 1 || result.hasMore) && <nav className="pagination" aria-label="Catalogue pages">
      <button disabled={page === 1} onClick={() => onPage(page - 1)}>Previous</button>
      <span>Page {page}</span>
      <button disabled={!result.hasMore} onClick={() => onPage(page + 1)}>Next</button>
    </nav>}
  </>
}

function CataloguePage({ username, roles, signOut }: { username: string; roles: string[]; signOut: () => void }) {
  const [staffOpen, setStaffOpen] = useState(false)
  const [offersOpen, setOffersOpen] = useState(false)
  const [chargesOpen, setChargesOpen] = useState(false)
  const [salesOpen, setSalesOpen] = useState(roles.includes('Operator'))
  const [stockOpen, setStockOpen] = useState(false)
  const [receivingMedicine, setReceivingMedicine] = useState<string | undefined>()
  const [adding, setAdding] = useState(false)
  const [savedMessage, setSavedMessage] = useState('')
  const [input, setInput] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [refresh, setRefresh] = useState(0)
  function submit(event: FormEvent) {
    event.preventDefault()
    setSearch(input.trim())
    setPage(1)
    setRefresh(value => value + 1)
  }
  return <div className="app-shell">
    <header className="app-header"><a className="brand" href="/"><span className="brand-mark" aria-hidden="true">＋</span>Pharmacy POS</a><div className="staff-bar"><span>{username} · {roles.join(', ')}</span><button onClick={signOut}>Sign out</button></div></header>
    <main>
      {staffOpen && roles.includes('Admin') ? <StaffManagement onBack={() => setStaffOpen(false)} /> : offersOpen && roles.includes('Admin') ? <OfferSettings onBack={() => setOffersOpen(false)} /> : chargesOpen && roles.includes('Admin') ? <ChargeSettings onBack={() => setChargesOpen(false)} /> : salesOpen ? <SalesCounter roles={roles} username={username} onBack={() => setSalesOpen(false)} /> : stockOpen ? <StockWorkspace isAdmin={roles.includes('Admin')} initialMedicineId={receivingMedicine} onBack={() => { setStockOpen(false); setReceivingMedicine(undefined) }} /> : <>
      <div className="page-heading"><div><p className="eyebrow">MEDICINES</p><h1>Medicine catalogue</h1><p className="subtitle">Find medicine details in one place.</p></div>{!adding && <div className="heading-actions"><button className="primary" onClick={() => setSalesOpen(true)}>Sales counter</button>{roles.includes('Admin') && <><button onClick={() => setStaffOpen(true)}>Staff accounts</button><button onClick={() => setChargesOpen(true)}>Cart charges</button><button onClick={() => setOffersOpen(true)}>Discount offers</button></>}<button onClick={() => { setReceivingMedicine(undefined); setStockOpen(true) }}>Stock & receiving</button><button className="primary" onClick={() => { setAdding(true); setSavedMessage('') }}>Add medicine</button></div>}</div>
      {savedMessage && <p className="save-confirmation" role="status">{savedMessage}</p>}
      {adding ? <AddMedicine isAdmin={roles.includes('Admin')} onCancel={() => { setAdding(false); setRefresh(v => v + 1) }} onSaved={(brand, status) => {
        setAdding(false); setSavedMessage(`${brand} ${status === 'Approved' ? 'was saved to the catalogue' : 'was submitted for admin approval'}.`)
        setInput(brand.slice(0, 100)); setSearch(brand.slice(0, 100)); setPage(1); setRefresh(value => value + 1)
      }} /> : <section className="catalogue-panel" aria-label="Medicine catalogue">
        <div className="toolbar">
          <form onSubmit={submit} className="search-form">
            <label htmlFor="medicine-search">Search medicines</label>
            <div className="search-controls"><input id="medicine-search" type="search" maxLength={100} placeholder="Brand, ingredient, or manufacturer" value={input} onChange={event => setInput(event.target.value)} /><button className="primary" type="submit">Search</button></div>
          </form>
          <button onClick={() => setRefresh(value => value + 1)}>Refresh</button>
        </div>
        <CatalogueResults key={JSON.stringify([search, page, refresh])} search={search} page={page} onPage={setPage} onReceive={id => { setReceivingMedicine(id); setStockOpen(true) }} />
      </section>}
      {!adding && <CatalogueSubmissions key={refresh} isAdmin={roles.includes('Admin')} onReviewed={() => setRefresh(v => v + 1)} />}
      <p className="page-note">Select Receive stock beside a medicine to record a delivery. Review approved quantities in Stock & receiving.</p>
      </>}
    </main>
  </div>
}
export default function App() {
  return <StaffSession>{(user, signOut) => <CataloguePage username={user.username} roles={user.roles} signOut={signOut} />}</StaffSession>
}
