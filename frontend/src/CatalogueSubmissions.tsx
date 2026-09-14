import { useEffect, useState } from 'react'

type Submission = {
  id: string; brandName: string; manufacturer: string; dosageForm: string; baseUnit: string;
  classification: string; reviewStatus: string; reviewNote: string | null; submittedBy: string;
  ingredients: { name: string; strengthValue: number; strengthUnit: string }[];
  batches: { batchNumber: string; manufacturingDate: string | null; expiryDate: string }[];
}
export function CatalogueSubmissions({ isAdmin, onReviewed }: { isAdmin: boolean; onReviewed: () => void }) {
  const [items, setItems] = useState<Submission[] | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [notes, setNotes] = useState<Record<string, string>>({})
  useEffect(() => {
    const controller = new AbortController()
    void fetch('/api/catalogue/submissions', { cache: 'no-store', signal: controller.signal }).then(r => {
      if (!r.ok) throw new Error('Could not load submissions. Select Refresh to retry.')
      return r.json() as Promise<Submission[]>
    }).then(setItems).catch(e => { if (!controller.signal.aborted) setError(e.message) })
    return () => controller.abort()
  }, [])
  async function review(id: string, approve: boolean) {
    setBusy(true); setError('')
    try {
      const sessionResponse = await fetch('/api/auth/session', { cache: 'no-store', signal: AbortSignal.timeout(10000) })
      if (!sessionResponse.ok) throw new Error('Could not check sign-in.')
      const session = await sessionResponse.json() as { csrfToken: string }
      const response = await fetch(`/api/catalogue/submissions/${id}/review`, {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken },
        body: JSON.stringify({ approve, note: notes[id] ?? '' }), signal: AbortSignal.timeout(15000),
      })
      if (!response.ok) {
        const body = await response.json().catch(() => ({})) as { message?: string }
        throw new Error(body.message ?? 'Review was not confirmed. Refresh to check its status.')
      }
      onReviewed()
    } catch (e) { setError(e instanceof Error ? e.message : 'Review failed.') }
    finally { setBusy(false) }
  }
  return <section className="catalogue-panel entry-panel" aria-label="Catalogue submissions">
    <h2>{isAdmin ? 'Medicine submissions to review' : 'My medicine submissions'}</h2>
    <p>Up to 100 entries are shown. Approving medicine details does not approve stock quantities or prices.</p>
    {error && <p role="alert" className="auth-error">{error}</p>}
    {!items && !error && <p>Loading submissions…</p>}
    {items?.length === 0 && <p>No pending submissions.</p>}
    {items?.map(item => <article className="submission-card" key={item.id}>
      <h3>{item.brandName} · {item.dosageForm}</h3>
      <p>{item.manufacturer} · {item.classification === 'Otc' ? 'OTC' : 'Prescription'} · Stock unit: {item.baseUnit}</p>
      <p>{item.ingredients.map(i => `${i.name} ${i.strengthValue} ${i.strengthUnit}`).join(' + ')}</p>
      <p>Entered by {item.submittedBy} · {item.reviewStatus === 'PendingReview' ? 'Pending review' : item.reviewStatus}</p>
      {item.batches.map(b => <p key={b.batchNumber}>Batch {b.batchNumber} · Manufactured: {b.manufacturingDate ?? 'Not recorded'} · Expires: {b.expiryDate}</p>)}
      {item.reviewNote && <p>Review note: {item.reviewNote}</p>}
      {isAdmin && item.reviewStatus === 'PendingReview' && <fieldset disabled={busy}>
        <legend className="sr-only">Review {item.brandName}</legend>
        <div className="field"><label htmlFor={`note-${item.id}`}>Review note (required to reject)</label><input id={`note-${item.id}`} maxLength={1000} value={notes[item.id] ?? ''} onChange={e => setNotes({ ...notes, [item.id]: e.target.value })} /></div>
        <div className="entry-actions"><button onClick={() => void review(item.id, false)}>Reject</button><button className="primary" onClick={() => void review(item.id, true)}>Approve medicine</button></div>
      </fieldset>}
    </article>)}
  </section>
}
