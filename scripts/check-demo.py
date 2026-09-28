#!/usr/bin/env python3
"""Read-only permissions/session smoke checks against the local fictional demo."""
import http.cookiejar,json,urllib.request,urllib.error
base='http://127.0.0.1:5168'
opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def call(path,data=None,csrf=None):
    req=urllib.request.Request(base+path,data=json.dumps(data).encode() if data is not None else None,headers={'Content-Type':'application/json',**({'X-CSRF-TOKEN':csrf} if csrf else {})})
    try:
        with opener.open(req) as r:return r.status,json.loads(r.read() or b'null')
    except urllib.error.HTTPError as e:return e.code,None
status,session=call('/api/auth/session');assert status==200 and session['demo'] and session['user'] is None
assert call('/api/demo/login/admin',{})[0]==400
assert call('/api/demo/login/owner',{},session['csrfToken'])[0]==400
assert call('/api/demo/login/operator',{},session['csrfToken'])[0]==204
_,session=call('/api/auth/session');assert session['user']['roles']==['Operator']
assert call('/api/barcodes?medicineId=00000000-0000-0000-0000-000000000000')[0]==403
assert call('/api/sales/stock')[0]==200
assert call('/api/auth/logout',{},session['csrfToken'])[0]==204
_,session=call('/api/auth/session')
assert call('/api/demo/login/admin',{},session['csrfToken'])[0]==204
_,session=call('/api/auth/session');assert session['user']['roles']==['Admin']
assert call('/api/staff')[0]==200
assert call('/api/staff',{},session['csrfToken'])[0]==403
assert call('/api/auth/logout',{},session['csrfToken'])[0]==204
print('Demo checks passed: CSRF, allowed roles, operator restrictions, admin entry, protected demo accounts and logout.')
