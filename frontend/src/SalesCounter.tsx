import { useEffect, useState } from 'react'
import { ReturnsWorkspace } from './ReturnsWorkspace'
import { CashForm, PendingCheckout, RecentSales } from './CashCheckout'
import type { CheckoutRequest } from './CashCheckout'
import { stockGet, stockPost } from './stockApi'

type Stock = { lotId: string; medicineId: string; brandName: string; manufacturer: string; dosageForm: string; classification: string; baseUnit: string; batchNumber: string; expiryDate: string; availableUnits: number; unitsPerStrip: number; unitsPerBox: number; mrpAmount: number; mrpUnit: string; mrpUnits: number; ingredients: { name: string; strengthValue: number; strengthUnit: string }[] }
type CartRow = { stock: Stock; unit: string; quantity: string }
type Quote = { quoteHash: string; lines: { lotId: string; unit: string; quantity: number; baseUnits: number; lineTotal: number; discountAmount: number; offerName: string | null; charges: { id: string; name: string; amount: number }[]; finalPrice: number; mrpAmount: number; mrpUnit: string; mrpUnits: number }[]; subtotal: number; discountTotal: number; chargeTotal: number; roundingAdjustment: number; estimatedTotal: number; quotedAt: string }
const money = (amount: number) => `৳${amount.toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 6 })}`
const unitName = (unit: string, stock: Stock) => unit === 'Piece' ? stock.baseUnit.toLowerCase() : unit.toLowerCase()
export function SalesCounter({ onBack, username, roles }: { onBack: () => void; username: string; roles: string[] }) {
  const [returnsOpen, setReturnsOpen] = useState(false)
  const pendingKey = `pharmacy-cash-checkout:${username}`
  const [pending, setPending] = useState<CheckoutRequest | null>(() => {
    try { const value = sessionStorage.getItem(pendingKey); return value ? JSON.parse(value) as CheckoutRequest : null } catch { return null }
  })
  const [history, setHistory] = useState(false), [checkoutError, setCheckoutError] = useState('')
  const [search, setSearch] = useState(''), [page, setPage] = useState(1), [refresh, setRefresh] = useState(0)
  const [cart, setCart] = useState<CartRow[]>([])
  const [message, setMessage] = useState('')
  function add(stock: Stock, unit: string, quantity: number) {
    setCart(current => {
      const found = current.findIndex(x => x.stock.lotId === stock.lotId && x.unit === unit)
      if (found >= 0) return current.map((row, i) => i === found ? { ...row, quantity: String(Number(row.quantity || 0) + quantity) } : row)
      return [...current, { stock, unit, quantity: String(quantity) }]
    })
    setMessage(`${quantity} ${unitName(unit, stock)}${quantity === 1 ? '' : 's'} of ${stock.brandName} added.`)
  }
  const fingerprint = JSON.stringify(cart.map(row => ({ lotId: row.stock.lotId, unit: row.unit, quantity: Number(row.quantity) })))
  function beginCheckout(request: CheckoutRequest) {
    try { sessionStorage.setItem(pendingKey, JSON.stringify(request)); setPending(request); setCheckoutError('') }
    catch { setCheckoutError('Allow this browser to store the pending checkout before completing a sale.') }
  }
  function clearPending(clearCart: boolean) {
    sessionStorage.removeItem(pendingKey); setPending(null); if (clearCart) setCart([]); setRefresh(v => v + 1)
  }
  if (pending) return <PendingCheckout request={pending} onNewSale={() => clearPending(true)} onRejected={() => clearPending(false)} />
  if (returnsOpen) return <ReturnsWorkspace isAdmin={roles.includes('Admin')} username={username} onBack={() => setReturnsOpen(false)} />
  if (history) return <RecentSales onBack={() => setHistory(false)} />
  return <>
    <div className="page-heading"><div><p className="eyebrow">SALES COUNTER</p><h1>New sale</h1><p className="subtitle">Find a medicine and build the customer’s cart.</p></div><div className="heading-actions"><button onClick={() => setHistory(true)}>Recent sales</button>{roles.some(r => r === 'Admin' || r === 'Operator') && <button onClick={() => setReturnsOpen(true)}>Returns & refunds</button>}<button onClick={onBack}>Medicine catalogue</button></div></div>
    <p className="counter-preview">Adding items does not reserve stock. Complete cash checkout to save the sale and deduct stock.</p>
    {checkoutError && <p role="alert" className="auth-error">{checkoutError}</p>}
    <div className="counter-layout">
      <section className="catalogue-panel counter-search" aria-label="Find medicines for sale">
        <div className="toolbar"><div className="search-form"><label htmlFor="counter-search">Search medicine, ingredient or manufacturer</label><div className="search-controls"><input id="counter-search" type="search" autoFocus maxLength={100} placeholder="e.g. Jardimet or metformin" value={search} onChange={e => { setSearch(e.target.value); setPage(1) }} /><button onClick={() => setRefresh(v => v + 1)}>Refresh stock</button></div></div></div>
        <p className="result-summary">Sellable batches only · earliest expiry first. Select the batch you will supply.</p>
        <StockSearch key={`${search}-${page}-${refresh}`} search={search} page={page} onPage={setPage} onAdd={add} disabled={cart.length >= 100} />
      </section>
      <section className="catalogue-panel counter-cart" aria-label="Sales cart">
        <div className="cart-heading"><h2>Customer cart</h2>{cart.length > 0 && <button onClick={() => { setCart([]); setMessage('Cart cleared.') }}>Clear cart</button>}</div>
        <p className="sr-only" role="status">{message}</p>
        {cart.length === 0 ? <div className="notice"><h3>Your cart is empty</h3><p>Choose a medicine on the left to begin.</p></div> : <>
          {cart.map((row, index) => <article className="cart-row" key={`${row.stock.lotId}-${row.unit}`}>
            <div><strong>{row.stock.brandName}</strong><span className="secondary">Batch {row.stock.batchNumber} · {unitName(row.unit, row.stock)}</span></div>
            <div className="cart-quantity"><label className="sr-only" htmlFor={`cart-count-${index}`}>{row.stock.brandName} {unitName(row.unit, row.stock)} quantity</label><input id={`cart-count-${index}`} type="number" min="1" max="1000000" step="1" value={row.quantity} onChange={e => { const value = e.target.value; setCart(rows => rows.map((r, i) => i === index ? { ...r, quantity: value } : r)) }} /><button aria-label={`Remove ${row.stock.brandName} ${unitName(row.unit, row.stock)}`} onClick={() => setCart(rows => rows.filter((_, i) => i !== index))}>Remove</button></div>
          </article>)}
          <CartQuote key={`${fingerprint}-${refresh}`} fingerprint={fingerprint} cart={cart} onBegin={beginCheckout} />
        </>}
      </section>
    </div>
  </>
}
function StockSearch({ search, page, onPage, onAdd, disabled }: { search: string; page: number; onPage: (page: number) => void; onAdd: (stock: Stock, unit: string, quantity: number) => void; disabled: boolean }) {
  const [result, setResult] = useState<{ items: Stock[]; hasMore: boolean } | null>(null), [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    const load = () => void stockGet<{ items: Stock[]; hasMore: boolean }>(`/api/sales/stock?${new URLSearchParams({ search, page: String(page) })}`, controller.signal).then(data => { setResult(data); setError('') }).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    const debounce = window.setTimeout(load, 200), timer = window.setInterval(load, 60000)
    return () => { controller.abort(); window.clearTimeout(debounce); window.clearInterval(timer) }
  }, [search, page])
  if (error) return <p className="auth-error" role="alert">{error}</p>
  if (!result) return <p className="notice" role="status">Finding available medicines…</p>
  return <>
    {result.items.length === 0 && <div className="notice"><h3>No sellable batches found</h3><p>Try a different search or check stock approval, expiry and MRP verification.</p></div>}
    {result.items.map(stock => <StockCard key={stock.lotId} stock={stock} onAdd={onAdd} disabled={disabled} />)}
    {(page > 1 || result.hasMore) && <nav className="pagination" aria-label="Sales search pages"><button disabled={page === 1} onClick={() => onPage(page - 1)}>Previous</button><span>Page {page}</span><button disabled={!result.hasMore} onClick={() => onPage(page + 1)}>Next</button></nav>}
  </>
}
function StockCard({ stock, onAdd, disabled }: { stock: Stock; onAdd: (stock: Stock, unit: string, quantity: number) => void; disabled: boolean }) {
  const [unit, setUnit] = useState('Piece'), [quantity, setQuantity] = useState('1')
  const pack = unit === 'Piece' ? 1 : unit === 'Strip' ? stock.unitsPerStrip : stock.unitsPerBox
  return <article className="counter-product">
    <div className="product-title"><h3>{stock.brandName}</h3><span className={`badge ${stock.classification === 'Prescription' ? 'prescription' : 'otc'}`}>{stock.classification === 'Prescription' ? 'Prescription' : 'OTC'}</span></div>
    <p>{stock.ingredients.map(i => `${i.name} ${i.strengthValue} ${i.strengthUnit}`).join(' + ')}</p>
    <p className="secondary">{stock.dosageForm} · {stock.manufacturer}</p>
    <p className="secondary">Batch {stock.batchNumber} · Expires {stock.expiryDate}</p>
    <div className="product-stock"><strong>{money(stock.mrpAmount / stock.mrpUnits)} / {stock.baseUnit.toLowerCase()}</strong><span>{stock.availableUnits.toLocaleString()} {stock.baseUnit.toLowerCase()}s available</span></div>
    <form className="product-add" onSubmit={e => { e.preventDefault(); if (Number.isInteger(Number(quantity)) && Number(quantity) > 0) onAdd(stock, unit, Number(quantity)) }}>
      <div className="field"><label htmlFor={`sale-unit-${stock.lotId}`}>Sell by</label><select id={`sale-unit-${stock.lotId}`} value={unit} onChange={e => setUnit(e.target.value)}><option value="Piece">{stock.baseUnit}</option>{stock.unitsPerStrip > 0 && <option value="Strip">Strip ({stock.unitsPerStrip})</option>}<option value="Box">Box ({stock.unitsPerBox})</option></select></div>
      <div className="field"><label htmlFor={`sale-count-${stock.lotId}`}>Quantity</label><input id={`sale-count-${stock.lotId}`} type="number" min="1" max={Math.min(1000000, Math.floor(stock.availableUnits / pack))} required step="1" value={quantity} onChange={e => setQuantity(e.target.value)} /></div>
      <button type="submit" className="primary" disabled={disabled || stock.availableUnits < pack}>Add to cart</button>
    </form>
  </article>
}
function CartQuote({ fingerprint, cart, onBegin }: { fingerprint: string; cart: CartRow[]; onBegin: (request: CheckoutRequest) => void }) {
  const [result, setResult] = useState<Quote | null>(null), [error, setError] = useState(''), [refresh, setRefresh] = useState(0)
  const valid = cart.every(row => Number.isInteger(Number(row.quantity)) && Number(row.quantity) >= 1 && Number(row.quantity) <= 1000000)
  useEffect(() => {
    if (!valid) return
    let active = true
    const timer = window.setTimeout(() => {
      void stockPost<Quote>('/api/sales/quote', { lines: JSON.parse(fingerprint) }).then(data => { if (active) setResult(data) }).catch(e => { if (active) setError(e.message) })
    }, 200)
    return () => { active = false; window.clearTimeout(timer) }
  }, [fingerprint, refresh, valid])
  useEffect(() => {
    const timer = window.setInterval(() => { setResult(null); setError(''); setRefresh(v => v + 1) }, 60000)
    return () => window.clearInterval(timer)
  }, [])
  return <div className="cart-totals">
    {!valid ? <p role="alert" className="auth-error">Enter positive whole-number quantities to calculate this cart.</p> : error ? <p role="alert" className="auth-error">{error}</p> : !result ? <p role="status">Checking stock and prices…</p> : <>
      <div className="cart-price-lines">{result.lines.map((line, i) => <div key={i}><p><span>{cart[i].stock.brandName} · {line.baseUnits} {cart[i].stock.baseUnit.toLowerCase()}s · MRP</span><strong>{money(line.lineTotal)}</strong></p>{line.offerName && <p className="secondary"><span>{line.offerName}</span><span>−{money(line.discountAmount)}</span></p>}{line.charges.map(c => <p key={c.id} className="secondary"><span>{c.name}</span><span>+{money(c.amount)}</span></p>)}<p><span>Final price</span><strong>{money(line.finalPrice)}</strong></p></div>)}</div>
      <dl><div><dt>MRP subtotal</dt><dd>{money(result.subtotal)}</dd></div><div><dt>Discounts</dt><dd>−{money(result.discountTotal)}</dd></div><div><dt>Charges</dt><dd>{money(result.chargeTotal)}</dd></div><div><dt>Rounding adjustment{result.roundingAdjustment !== 0 ? result.roundingAdjustment > 0 ? ' (up)' : ' (down)' : ''}</dt><dd>{result.roundingAdjustment > 0 ? '+' : ''}{money(result.roundingAdjustment)}</dd></div><div className="cart-total"><dt>Estimated total</dt><dd>{money(result.estimatedTotal)}</dd></div></dl>
      <p className="field-help">Offers, stock and prices checked at {new Date(result.quotedAt).toLocaleTimeString()}.</p>
      <CashForm key={`${result.quoteHash}-${refresh}`} total={result.estimatedTotal} quoteHash={result.quoteHash} fingerprint={fingerprint} onBegin={onBegin} />
    </>}
    <button onClick={() => { setResult(null); setError(''); setRefresh(v => v + 1) }}>Refresh cart prices</button>
    <p className="field-help">Stock will be checked again when you complete the sale.</p>
  </div>
}
