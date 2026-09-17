import { useEffect, useRef, useState } from 'react'
import { stockGet, stockPost } from './stockApi'
export type Branding = { id: string | null; name: string; address: string; logo: string | null; policy: string }
export function ReceiptHeader({ branding }: { branding: Branding }) {
  return <header className="receipt-branding">{branding.logo && <img src={branding.logo} alt="Pharmacy logo" />}<h1>{branding.name}</h1><p>{branding.address}</p></header>
}
export function ReceiptSettings({ onBack }: { onBack: () => void }) {
  const saveRef = useRef<{ key: string; id: string } | null>(null)
  const [data, setData] = useState<Branding | null>(null), [error, setError] = useState(''), [message, setMessage] = useState(''), [busy, setBusy] = useState(false)
  useEffect(() => { void stockGet<Branding>('/api/receipt-settings').then(setData).catch(e => setError(e.message)) }, [])
  return <section className="catalogue-panel charge-form"><div className="page-heading"><h1>Receipt settings</h1><button onClick={onBack}>Back to catalogue</button></div>
    <p>80 mm thermal receipt. Saved changes apply to new sales; past receipts keep their original details.</p>
    {error && <p role="alert" className="auth-error">{error}</p>}{message && <p role="status">{message}</p>}
    {data && <><form onSubmit={async e => { e.preventDefault(); setBusy(true); setError(''); setMessage(''); try { const key = JSON.stringify(data); if (saveRef.current?.key !== key) saveRef.current = { key, id: crypto.randomUUID() }; setData(await stockPost<Branding>('/api/receipt-settings', { id: saveRef.current.id, expectedId: data.id, name: data.name, address: data.address, logo: data.logo })); setMessage('Receipt settings saved.') } catch (e) { setError((e as Error).message) } finally { setBusy(false) } }}>
      <fieldset disabled={busy} className="charge-fields"><label>Pharmacy or hospital name<input required maxLength={200} value={data.name} onChange={e => setData({ ...data, name: e.target.value })} /></label>
      <label>Address<textarea required maxLength={600} value={data.address} onChange={e => setData({ ...data, address: e.target.value })} /></label>
      <label>Logo (PNG, up to 100 KB, 1024 × 1024 pixels)<input type="file" accept="image/png" onChange={async e => { const file = e.target.files?.[0]; if (!file) return; setError(''); if (file.type !== 'image/png' || file.size > 102400) { setError('Choose a PNG image up to 100 KB.'); return }
        try { const logo = await new Promise<string>((resolve, reject) => { const reader = new FileReader(); reader.onload = () => resolve(String(reader.result)); reader.onerror = reject; reader.readAsDataURL(file) }); const img = new Image(); img.src = logo; await img.decode(); if (img.width > 1024 || img.height > 1024) throw new Error('Logo must be at most 1024 × 1024 pixels.'); setData(previous => previous ? { ...previous, logo } : previous) } catch { setError('Choose a valid PNG image at most 1024 × 1024 pixels.') }
      }} /></label>{data.logo && <button type="button" onClick={() => setData({ ...data, logo: null })}>Remove logo</button>}
      <p>{data.policy}</p><button className="primary">{busy ? 'Saving…' : 'Save receipt settings'}</button></fieldset></form>
      <h2>Header preview</h2><div className="receipt-settings-preview"><ReceiptHeader branding={data} /></div><p className="field-help">For printing, select 80 mm paper, 100% scale, and turn off browser headers and footers. You can use Save as PDF before connecting a printer.</p>
    </>}
  </section>
}
