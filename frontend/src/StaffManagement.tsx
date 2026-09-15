import { useEffect, useRef, useState } from 'react'
import { stockGet, stockPost } from './stockApi'

type Staff = { id: string; username: string; roles: string[]; disabled: boolean; lockedUntil: string | null; version: string; manageable: boolean }
type Event = { id: string; action: string; reason: string; at: string; actor: string }
const hint = 'At least 12 characters, including uppercase, lowercase, a number and a symbol.'
export function StaffManagement({ onBack }: { onBack: () => void }) {
  const [refresh, setRefresh] = useState(0), [search, setSearch] = useState(''), [page, setPage] = useState(1)
  const [name, setName] = useState(''), [password, setPassword] = useState(''), [confirm, setConfirm] = useState(''), [busy, setBusy] = useState(false), [error, setError] = useState(''), [message, setMessage] = useState('')
  const request = useRef(crypto.randomUUID())
  function edited() { request.current = crypto.randomUUID() }
  return <><div className="page-heading"><div><p className="eyebrow">ADMIN</p><h1>Staff accounts</h1></div><button onClick={onBack}>Medicine catalogue</button></div>
    <form className="catalogue-panel charge-form" onSubmit={async e => { e.preventDefault(); setError(''); setMessage(''); if(password !== confirm) { setError('The passwords do not match.'); return } setBusy(true); try { await stockPost('/api/staff',{requestId:request.current,username:name,password}); setName('');setPassword('');setConfirm('');edited();setMessage('Operator account created. Share the username and password privately with that staff member.');setRefresh(v=>v+1) } catch(e) { setError((e as Error).message) } finally {setBusy(false)} }}>
      <h2>Create operator account</h2><p>Each staff member should use their own account so sales, returns and stock entries have a clear owner.</p><fieldset className="charge-fields" disabled={busy}>
        <label>Username<input required maxLength={100} autoComplete="off" value={name} onChange={e=>{setName(e.target.value);edited()}} /></label>
        <label>Initial password<input required type="password" minLength={12} maxLength={1024} autoComplete="new-password" value={password} onChange={e=>{setPassword(e.target.value);edited()}} /></label>
        <label>Confirm initial password<input required type="password" minLength={12} maxLength={1024} autoComplete="new-password" value={confirm} onChange={e=>setConfirm(e.target.value)} /></label><p className="field-help">{hint}</p><button className="primary">{busy?'Creating…':'Create operator account'}</button>
      </fieldset>{error&&<p role="alert" className="auth-error">{error}</p>}{message&&<p role="status" className="save-confirmation">{message}</p>}
    </form>
    <section className="catalogue-panel charge-form"><h2>Staff list</h2><div className="field"><label htmlFor="staff-search">Search usernames</label><input id="staff-search" type="search" maxLength={100} value={search} onChange={e=>{setSearch(e.target.value);setPage(1)}} /></div><button onClick={()=>setRefresh(v=>v+1)}>Refresh staff</button><StaffList key={`${refresh}-${page}-${search}`} search={search} page={page} onPage={setPage} onChanged={()=>setRefresh(v=>v+1)} /></section>
  </>
}
function StaffList({search,page,onPage,onChanged}:{search:string;page:number;onPage:(n:number)=>void;onChanged:()=>void}) {
  const [data,setData]=useState<{items:Staff[];hasMore:boolean}|null>(null),[error,setError]=useState('')
  useEffect(()=>{const controller=new AbortController();const timer=window.setTimeout(()=>void stockGet<NonNullable<typeof data>>(`/api/staff?${new URLSearchParams({search,page:String(page)})}`,controller.signal).then(setData).catch(e=>{if(!controller.signal.aborted)setError(e.message)}),200);return()=>{controller.abort();window.clearTimeout(timer)}},[search,page])
  if(error)return <p role="alert">{error}</p>
  if(!data)return <p>Loading staff…</p>
  return <>{!data.items.length&&<p>No matching accounts.</p>}{data.items.map(s=><StaffCard key={s.id} staff={s} onChanged={onChanged}/>)}<div className="pagination"><button disabled={page===1} onClick={()=>onPage(page-1)}>Previous</button><span>Page {page}</span><button disabled={!data.hasMore} onClick={()=>onPage(page+1)}>Next</button></div></>
}
function StaffCard({staff,onChanged}:{staff:Staff;onChanged:()=>void}) {
  const [action,setAction]=useState(''),[history,setHistory]=useState<Event[]|null>(null),[error,setError]=useState('')
  return <article className="counter-product"><h3>{staff.username} · {staff.roles.join(', ')}</h3><p>{staff.disabled?'Disabled':staff.lockedUntil?`Temporarily locked until ${new Date(staff.lockedUntil).toLocaleString()}`:'Active'}</p>
    {staff.manageable?<div className="heading-actions"><button onClick={()=>setAction('ResetPassword')}>Reset password for {staff.username}</button><button onClick={()=>setAction(staff.disabled?'Enable':'Disable')}>{staff.disabled?'Enable':'Disable'} {staff.username}</button></div>:<p className="field-help">Protected account — only operators can be managed here.</p>}
    {action&&<ChangeForm key={action} staff={staff} action={action} onCancel={()=>setAction('')} onChanged={onChanged}/>}
    <button onClick={async()=>{setError('');try{setHistory(await stockGet<Event[]>(`/api/staff/${staff.id}/history`))}catch(e){setError((e as Error).message)}}}>View history for {staff.username}</button>{error&&<p role="alert">{error}</p>}{history&&<div>{history.length===0?<p>No account-management events recorded.</p>:history.map(e=><p key={e.id}>{e.action} · {e.actor} · {new Date(e.at).toLocaleString()} · {e.reason}</p>)}</div>}
  </article>
}
function ChangeForm({staff,action,onCancel,onChanged}:{staff:Staff;action:string;onCancel:()=>void;onChanged:()=>void}) {
  const [password,setPassword]=useState(''),[confirm,setConfirm]=useState(''),[reason,setReason]=useState(''),[checked,setChecked]=useState(false),[busy,setBusy]=useState(false),[error,setError]=useState('')
  const request=useRef(crypto.randomUUID())
  function edited(){request.current=crypto.randomUUID();setChecked(false)}
  return <form onSubmit={async e=>{e.preventDefault();setError('');if(action==='ResetPassword'&&password!==confirm){setError('The passwords do not match.');return}setBusy(true);try{await stockPost(`/api/staff/${staff.id}/change`,{requestId:request.current,expectedVersion:staff.version,action,password:action==='ResetPassword'?password:null,reason});setPassword('');setConfirm('');onChanged()}catch(e){setError((e as Error).message)}finally{setBusy(false)}}}><fieldset disabled={busy} className="charge-fields"><h4>{action==='ResetPassword'?'Reset password':action} · {staff.username}</h4>
    {action==='ResetPassword'&&<><label>New password<input required type="password" autoComplete="new-password" minLength={12} maxLength={1024} value={password} onChange={e=>{setPassword(e.target.value);edited()}}/></label><label>Confirm new password<input required type="password" autoComplete="new-password" value={confirm} onChange={e=>setConfirm(e.target.value)}/></label><p>{hint} This clears temporary login lockout but does not enable a disabled account.</p></>}
    <label>Reason<textarea required maxLength={1000} value={reason} onChange={e=>{setReason(e.target.value);edited()}}/></label>
    <label><input type="checkbox" required checked={checked} onChange={e=>setChecked(e.target.checked)}/> I confirm this change to {staff.username}. Existing sessions will be signed out.</label>
    <div className="heading-actions"><button className="primary" disabled={!checked}>{busy?'Saving…':'Confirm account change'}</button><button type="button" onClick={onCancel}>Cancel</button></div></fieldset>{error&&<p role="alert" className="auth-error">{error}</p>}</form>
}
