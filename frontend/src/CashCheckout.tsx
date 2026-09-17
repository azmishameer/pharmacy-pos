import { ReceiptHeader, type Branding } from './ReceiptSettings'
import { Fragment, useEffect, useRef, useState } from 'react'
import { stockGet } from './stockApi'

export type CheckoutRequest = { requestId: string; lines: { lotId: string; quantity: number; unit: string }[]; quoteHash: string; cashReceived: number; payments?: { method: string; amount: number; tendered: number | null; reference: string | null; confirmed: boolean }[] }
type Receipt = { branding: Branding; returnDeadline: string; id: string; receiptNumber: string; completedAt: string; operatorName: string; total: number; currency: string;
  payments: { method: string; amount: number; tendered: number; change: number; reference: string | null }[];
  pricing: { subtotal: number; discountTotal: number; chargeTotal: number; totalBeforeRounding: number; roundingAdjustment: number;
    lines: { brandName: string; batchNumber: string; quantity: number; unit: string; baseUnits: number; baseUnit: string; lineTotal: number; finalPrice: number; discountAmount: number; offerName: string | null; charges: { id: string; name: string; amount: number }[] }[] } }
const money = (n: number) => `৳${n.toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 6 })}`
export function CashForm({ total, quoteHash, fingerprint, onBegin }: { total: number; quoteHash: string; fingerprint: string; onBegin: (request: CheckoutRequest) => void }) {
  const methods = ['Cash', 'Card', 'bKash', 'Nagad']
  const submitted = useRef<{ key: string; request: CheckoutRequest } | null>(null)
  const [verifiedQuote, setVerifiedQuote] = useState<string | null>(null)
  const verified = (quote: string, checked: boolean) => checked && verifiedQuote === quote
  const [rows, setRows] = useState([{ method: 'Cash', amount: total.toFixed(2), tendered: '', reference: '' }]), [confirmed, setConfirmed] = useState(false)
  const cents = (value: string) => Math.round(Number(value) * 100)
  const validMoney = (value: string) => value.trim() !== '' && Number.isFinite(Number(value)) && Number(value) >= 0 && Number(value) <= 1000000000000 && Math.abs(Number(value) * 100 - cents(value)) < .0001
  const assigned = rows.reduce((sum, row) => sum + cents(row.amount), 0), remaining = Math.round(total * 100) - assigned
  const valid = remaining === 0 && rows.every(row => validMoney(row.amount) && (Number(row.amount) > 0 || total === 0 && rows.length === 1 && row.method === 'Cash') && (row.method === 'Cash' ? validMoney(row.tendered) && cents(row.tendered) >= cents(row.amount) : row.reference.trim().length > 0 && row.reference.trim().length <= 100))
  function change(index: number, values: Partial<typeof rows[number]>) { setRows(previous => previous.map((row, i) => i === index ? { ...row, ...values } : row)); setConfirmed(false) }
  return <form className="cash-checkout" onSubmit={e => { e.preventDefault(); if (!valid || !verified(quoteHash, confirmed)) return
    const payments = rows.map(row => ({ method: row.method, amount: Number(row.amount), tendered: row.method === 'Cash' ? Number(row.tendered) : null, reference: row.method === 'Cash' ? null : row.reference.trim(), confirmed: true }))
    const key = JSON.stringify([fingerprint, quoteHash, payments]); if (submitted.current?.key !== key) submitted.current = { key, request: { requestId: crypto.randomUUID(), lines: JSON.parse(fingerprint), quoteHash, cashReceived: 0, payments } }; onBegin(submitted.current.request)
  }}>
    <h3>Payment</h3><p>Bill total: <strong>{money(total)}</strong></p>
    {rows.map((row, i) => <fieldset className="payment-row" key={i}><legend>Payment {i + 1}</legend>
      <label>Payment method<select value={row.method} onChange={e => change(i, { method: e.target.value, tendered: '', reference: '' })}>{methods.map(method => <option key={method} value={method} disabled={method !== row.method && rows.some(r => r.method === method)}>{method}</option>)}</select></label>
      <label>Amount applied to bill (৳)<input type="number" required min={total === 0 ? 0 : .01} step="0.01" max={total} value={row.amount} onChange={e => change(i, { amount: e.target.value })} /></label>
      {row.method === 'Cash' ? <><label>Cash received (৳)<input type="number" required min={Number(row.amount)} max="1000000000000" step="0.01" value={row.tendered} onChange={e => change(i, { tendered: e.target.value })} /></label><p>Change: <strong>{validMoney(row.tendered) && cents(row.tendered) >= cents(row.amount) ? money((cents(row.tendered) - cents(row.amount)) / 100) : '—'}</strong></p></> : <label>{row.method === 'Card' ? 'Terminal transaction reference (not card number)' : `${row.method} transaction ID`}<input required maxLength={100} autoComplete="off" value={row.reference} onChange={e => change(i, { reference: e.target.value })} /></label>}
      {rows.length > 1 && <button type="button" onClick={() => { setRows(previous => previous.filter((_, n) => n !== i)); setConfirmed(false) }}>Remove payment {i + 1}</button>}
    </fieldset>)}
    <button type="button" disabled={rows.length === 4 || total === 0} onClick={() => { setRows(previous => [...previous, { method: methods.find(method => !previous.some(row => row.method === method))!, amount: Math.max(0, remaining / 100).toFixed(2), tendered: '', reference: '' }]); setConfirmed(false) }}>Add another payment method</button>
    <p role="status">{Number.isFinite(remaining) ? remaining >= 0 ? `Remaining to allocate: ${money(remaining / 100)}` : `Reduce payment amounts by ${money(-remaining / 100)}` : 'Enter valid payment amounts.'}</p>
    <p className="field-help">Card, bKash and Nagad payments are processed separately. Check successful payment on your terminal or merchant account, then record its unique reference here. This form does not charge the customer.</p>
    <label><input type="checkbox" required checked={verified(quoteHash, confirmed)} onChange={e => { setVerifiedQuote(quoteHash); setConfirmed(e.target.checked) }} /> I have verified every payment, received any cash and checked the items.</label>
    {confirmed && !verified(quoteHash, confirmed) && <p role="alert">Stock or prices changed. Review the bill and confirm payment again. Your entered references have been kept.</p>}
    <button className="primary" disabled={!valid || !verified(quoteHash, confirmed)} type="submit">Complete sale · {money(total)}</button>
    <p className="field-help">Completing the sale saves the receipt and deducts stock once. Printing does not make another sale.</p>
  </form>
}
export function PendingCheckout({ request, onNewSale, onRejected }: { request: CheckoutRequest; onNewSale: () => void; onRejected: () => void }) {
  const [receipt, setReceipt] = useState<Receipt | null>(null), [error, setError] = useState(''), [rejected, setRejected] = useState(false), [retry, setRetry] = useState(0), [busy, setBusy] = useState(true)
  useEffect(() => {
    let active = true
    async function finish() {
      try {
        const session = await stockGet<{ csrfToken: string }>('/api/auth/session')
        const r = await fetch('/api/sales/checkout', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken }, body: JSON.stringify(request), signal: AbortSignal.timeout(20000) })
        const data = await r.json().catch(() => null)
        if (!active) return
        if (r.ok && data?.receiptNumber) setReceipt(data as Receipt)
        else { setRejected(r.status === 400 || r.status === 409); setError(data?.message ?? 'The sale result is not confirmed. Retry this checkout after checking your connection and sign-in.') }
      } catch { if (active) setError('The sale result is not confirmed. Retry this same checkout to recover its receipt. Do not collect payment again.') }
      finally { if (active) setBusy(false) }
    }
    void finish(); return () => { active = false }
  }, [request, retry])
  if (receipt) return <><ReceiptView receipt={receipt} /><button className="primary no-print" onClick={onNewSale}>Start new sale</button></>
  return <section className="catalogue-panel charge-form"><h1>{busy ? 'Completing sale…' : rejected ? 'Checkout needs attention' : 'Confirming your sale'}</h1><p>Checkout reference: {request.requestId}</p>
    {error && <p role="alert" className="auth-error">{error}</p>}
    {!busy && (rejected ? <button onClick={onRejected}>Return to cart and refresh prices</button> : <button className="primary" onClick={() => { setError(''); setBusy(true); setRetry(v => v + 1) }}>Retry this checkout</button>)}
    <p>Keep this reference until the receipt is confirmed. A retry uses the same sale reference and cannot deduct stock twice.</p>
  </section>
}
export function ReceiptView({ receipt }: { receipt: Receipt }) {
  return <><section className="receipt-paper" aria-label="Sale receipt"><ReceiptHeader branding={receipt.branding} /><h2>Sale receipt</h2><p><strong>{receipt.receiptNumber}</strong></p><p>{new Date(receipt.completedAt).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' })} · Bangladesh time</p><p>Operator: {receipt.operatorName}</p>
    {receipt.pricing.lines.map((l, i) => <article key={i} className="receipt-line"><strong>{l.brandName}</strong><p>Batch {l.batchNumber} · {l.quantity} {l.unit.toLowerCase()} · {l.baseUnits} {l.baseUnit.toLowerCase()}s</p><p>MRP amount: {money(l.lineTotal)}</p>{l.offerName && <p>{l.offerName}: −{money(l.discountAmount)}</p>}{l.charges.map(c => <p key={c.id}>{c.name}: +{money(c.amount)}</p>)}<p><strong>Line total: {money(l.finalPrice)}</strong></p></article>)}
    <dl className="receipt-totals"><div><dt>MRP subtotal</dt><dd>{money(receipt.pricing.subtotal)}</dd></div><div><dt>Discounts</dt><dd>−{money(receipt.pricing.discountTotal)}</dd></div><div><dt>Charges</dt><dd>{money(receipt.pricing.chargeTotal)}</dd></div><div><dt>Before rounding</dt><dd>{money(receipt.pricing.totalBeforeRounding)}</dd></div><div><dt>Rounding adjustment {receipt.pricing.roundingAdjustment > 0 ? '(up)' : receipt.pricing.roundingAdjustment < 0 ? '(down)' : ''}</dt><dd>{receipt.pricing.roundingAdjustment > 0 ? '+' : ''}{money(receipt.pricing.roundingAdjustment)}</dd></div><div><dt><strong>Total paid</strong></dt><dd><strong>{money(receipt.total)}</strong></dd></div>
      {receipt.payments.map((p, i) => <Fragment key={i}><div><dt>{p.method} paid</dt><dd>{money(p.amount)}</dd></div>{p.method === 'Cash' && <><div><dt>Cash received</dt><dd>{money(p.tendered)}</dd></div><div><dt>Change</dt><dd>{money(p.change)}</dd></div></>}{p.reference && <div className="receipt-reference"><dt>{p.method} reference</dt><dd>{p.reference}</dd></div>}</Fragment>)}
    </dl><p>Sale completed · BDT</p><footer className="receipt-policy"><p>{receipt.branding.policy}</p><p>Return deadline: {new Date(receipt.returnDeadline).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' })} (Bangladesh time)</p></footer></section><button className="no-print" onClick={() => window.print()}>Print receipt</button></>
}
export function RecentSales({ onBack }: { onBack: () => void }) {
  const [page, setPage] = useState(1), [rows, setRows] = useState<{ items: { id: string; number: number; completedAt: string; total: number; operatorName: string }[]; hasMore: boolean } | null>(null), [receipt, setReceipt] = useState<Receipt | null>(null), [error, setError] = useState('')
  useEffect(() => {
    const controller = new AbortController()
    void stockGet<NonNullable<typeof rows>>(`/api/sales/receipts?page=${page}`, controller.signal).then(setRows).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    return () => controller.abort()
  }, [page])
  if (receipt) return <><ReceiptView receipt={receipt} /><button className="no-print" onClick={() => setReceipt(null)}>Back to recent sales</button></>
  return <section className="catalogue-panel charge-form"><div className="page-heading"><h1>Recent sales</h1><button onClick={onBack}>Back to counter</button></div>{error && <p role="alert">{error}</p>}{!rows ? <p>Loading sales…</p> : <>{rows.items.length === 0 && <p>No completed sales yet.</p>}{rows.items.map(r => <article className="counter-product" key={r.id}><strong>POS-{String(r.number).padStart(8, '0')} · {money(r.total)}</strong><p>{new Date(r.completedAt).toLocaleString()} · {r.operatorName}</p><button onClick={async () => { setError(''); try { setReceipt(await stockGet<Receipt>(`/api/sales/receipts/${r.id}`)) } catch (e) { setError((e as Error).message) } }}>View receipt</button></article>)}<div className="pagination"><button disabled={page === 1} onClick={() => { setRows(null); setPage(p => p - 1) }}>Previous</button><span>Page {page}</span><button disabled={!rows.hasMore} onClick={() => { setRows(null); setPage(p => p + 1) }}>Next</button></div></>}</section>
}
