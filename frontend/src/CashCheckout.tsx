import { useEffect, useRef, useState } from 'react'
import { stockGet } from './stockApi'

export type CheckoutRequest = { requestId: string; lines: { lotId: string; quantity: number; unit: string }[]; quoteHash: string; cashReceived: number }
type Receipt = { id: string; receiptNumber: string; completedAt: string; operatorName: string; total: number; currency: string;
  payments: { method: string; amount: number; tendered: number; change: number }[];
  pricing: { subtotal: number; discountTotal: number; chargeTotal: number; totalBeforeRounding: number; roundingAdjustment: number;
    lines: { brandName: string; batchNumber: string; quantity: number; unit: string; baseUnits: number; baseUnit: string; lineTotal: number; finalPrice: number; discountAmount: number; offerName: string | null; charges: { id: string; name: string; amount: number }[] }[] } }
const money = (n: number) => `৳${n.toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 6 })}`
export function CashForm({ total, quoteHash, fingerprint, onBegin }: { total: number; quoteHash: string; fingerprint: string; onBegin: (request: CheckoutRequest) => void }) {
  const submitted = useRef<{ key: string; request: CheckoutRequest } | null>(null)
  const [cash, setCash] = useState(''), [confirmed, setConfirmed] = useState(false)
  const amount = Number(cash), valid = cash !== '' && Number.isFinite(amount) && amount >= total && amount <= 1000000000000 && Math.abs(Math.round(amount * 100) - amount * 100) < 0.0001
  return <form className="cash-checkout" onSubmit={e => { e.preventDefault(); if (valid && confirmed) { const key = JSON.stringify([fingerprint, quoteHash, amount]); if (submitted.current?.key !== key) submitted.current = { key, request: { requestId: crypto.randomUUID(), lines: JSON.parse(fingerprint), quoteHash, cashReceived: amount } }; onBegin(submitted.current.request) } }}>
    <h3>Cash checkout</h3><label htmlFor="cash-received">Cash received (৳)</label><input id="cash-received" type="number" required min={total} max="1000000000000" step="0.01" value={cash} onChange={e => { setCash(e.target.value); setConfirmed(false) }} />
    <p>Change: <strong>{valid ? money(Math.round((amount - total) * 100) / 100) : '—'}</strong></p>
    <label><input type="checkbox" required checked={confirmed} onChange={e => setConfirmed(e.target.checked)} /> I have received the cash and checked the items.</label>
    <button className="primary" disabled={!valid || !confirmed} type="submit">Complete cash sale · {money(total)}</button>
    <p className="field-help">Completing the sale saves the receipt and deducts stock. Printing alone does not make another sale.</p>
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
      } catch { if (active) setError('The sale result is not confirmed. Retry this same checkout to recover its receipt. Do not collect cash again.') }
      finally { if (active) setBusy(false) }
    }
    void finish(); return () => { active = false }
  }, [request, retry])
  if (receipt) return <><ReceiptView receipt={receipt} /><button className="primary no-print" onClick={onNewSale}>Start new sale</button></>
  return <section className="catalogue-panel charge-form"><h1>{busy ? 'Completing cash sale…' : rejected ? 'Checkout needs attention' : 'Confirming your sale'}</h1><p>Checkout reference: {request.requestId}</p>
    {error && <p role="alert" className="auth-error">{error}</p>}
    {!busy && (rejected ? <button onClick={onRejected}>Return to cart and refresh prices</button> : <button className="primary" onClick={() => { setError(''); setBusy(true); setRetry(v => v + 1) }}>Retry this checkout</button>)}
    <p>Keep this reference until the receipt is confirmed. A retry uses the same sale reference and cannot deduct stock twice.</p>
  </section>
}
export function ReceiptView({ receipt }: { receipt: Receipt }) {
  return <><section className="receipt-paper" aria-label="Sale receipt"><h1>Pharmacy POS</h1><h2>Cash sale receipt</h2><p><strong>{receipt.receiptNumber}</strong></p><p>{new Date(receipt.completedAt).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' })} · Bangladesh time</p><p>Operator: {receipt.operatorName}</p>
    {receipt.pricing.lines.map((l, i) => <article key={i} className="receipt-line"><strong>{l.brandName}</strong><p>Batch {l.batchNumber} · {l.quantity} {l.unit.toLowerCase()} · {l.baseUnits} {l.baseUnit.toLowerCase()}s</p><p>MRP amount: {money(l.lineTotal)}</p>{l.offerName && <p>{l.offerName}: −{money(l.discountAmount)}</p>}{l.charges.map(c => <p key={c.id}>{c.name}: +{money(c.amount)}</p>)}<p><strong>Line total: {money(l.finalPrice)}</strong></p></article>)}
    <dl className="receipt-totals"><div><dt>MRP subtotal</dt><dd>{money(receipt.pricing.subtotal)}</dd></div><div><dt>Discounts</dt><dd>−{money(receipt.pricing.discountTotal)}</dd></div><div><dt>Charges</dt><dd>{money(receipt.pricing.chargeTotal)}</dd></div><div><dt>Before rounding</dt><dd>{money(receipt.pricing.totalBeforeRounding)}</dd></div><div><dt>Rounding adjustment {receipt.pricing.roundingAdjustment > 0 ? '(up)' : receipt.pricing.roundingAdjustment < 0 ? '(down)' : ''}</dt><dd>{receipt.pricing.roundingAdjustment > 0 ? '+' : ''}{money(receipt.pricing.roundingAdjustment)}</dd></div><div><dt><strong>Total paid</strong></dt><dd><strong>{money(receipt.total)}</strong></dd></div>
      {receipt.payments.map((p, i) => <div key={i}><dt>{p.method} received / change</dt><dd>{money(p.tendered)} / {money(p.change)}</dd></div>)}
    </dl><p>Sale completed · BDT</p></section><button className="no-print" onClick={() => window.print()}>Print receipt</button></>
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
