import { useState } from 'react'
import { stockExport, stockGet } from './stockApi'

type Totals = { salesCount: number; refundCount: number; subtotal: number; discounts: number; charges: number; rounding: number; sales: number; refunds: number; netSales: number; cashCollected: number; cashRefunded: number; netCash: number }
type Report = { from: string; to: string; timeZone: string; generatedAt: string; totals: Totals; staff: { staffId: string; username: string; totals: Totals }[] }
const money = (n: number) => `৳${n.toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 6 })}`
const today = () => new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Dhaka', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
export function SalesReport({ onBack }: { onBack: () => void }) {
  const [from, setFrom] = useState(today), [to, setTo] = useState(today), [report, setReport] = useState<Report | null>(null)
  const [busy, setBusy] = useState(false), [error, setError] = useState('')
  return <><div className="page-heading"><div><p className="eyebrow">ADMIN</p><h1>Sales report</h1><p>Sales and refunds by transaction date in Bangladesh time.</p></div><button onClick={onBack}>Medicine catalogue</button></div>
    <form className="catalogue-panel charge-form" onSubmit={async e => { e.preventDefault(); setBusy(true); setError(''); setReport(null); try { setReport(await stockGet<Report>(`/api/reports/sales?${new URLSearchParams({ from, to })}`)) } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}>
      <fieldset disabled={busy} className="charge-fields"><label>From date<input type="date" required value={from} min="2000-01-01" max={to} onInput={e => setFrom(e.currentTarget.value)} /></label><label>To date<input type="date" required min={from} value={to} onInput={e => setTo(e.currentTarget.value)} /></label><button className="primary">{busy ? 'Loading…' : 'View report'}</button></fieldset>
      <p className="field-help">Both dates are included. Select up to 366 days.</p>
    </form>
    {error && <p role="alert" className="auth-error">{error}</p>}
    {report && <section className="catalogue-panel charge-form"><h2>{report.from} to {report.to}</h2><p>Generated {new Date(report.generatedAt).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' })} · Asia/Dhaka</p>
      <button disabled={busy} onClick={async () => { setBusy(true); setError(''); try { await stockExport(`/api/reports/sales?${new URLSearchParams({ from: report.from, to: report.to, format: 'csv' })}`, `sales-${report.from}-${report.to}.csv`) } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}>Export CSV</button>
      <p className="field-help">Export uses these report dates and includes transactions completed since this view was loaded.</p>
      <Summary totals={report.totals} />
      <p>Refunds include payments made during this period for earlier sales. Discounts, charges and rounding describe sales completed during this period, before refunds.</p>
      <p>Cash collected is the amount retained after giving change. Net cash excludes opening float, expenses and other cash movements. Net sales is sales minus refunds; it is not profit.</p>
      <h2>Activity by staff member</h2>{report.staff.length === 0 ? <p>No sales or refunds in this period.</p> : report.staff.map(s => <article className="counter-product" key={s.staffId}><h3>{s.username}</h3><Summary totals={s.totals} /></article>)}
      <p className="field-help">Sales belong to the cashier who completed them; refunds belong to the staff member who issued them.</p>
    </section>}
  </>
}
function Summary({ totals: t }: { totals: Totals }) {
  return <dl className="report-totals">{[
    ['Completed sales', String(t.salesCount)], ['Refund transactions', String(t.refundCount)], ['MRP subtotal', money(t.subtotal)], ['Discounts', money(t.discounts)], ['Charges', money(t.charges)], ['Rounding adjustment', money(t.rounding)],
    ['Sales total', money(t.sales)], ['Refunds', money(t.refunds)], ['Net sales', money(t.netSales)], ['Cash collected', money(t.cashCollected)], ['Cash refunded', money(t.cashRefunded)], ['Net cash', money(t.netCash)],
  ].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>
}
