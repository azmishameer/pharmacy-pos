export async function stockGet<T>(url: string, signal?: AbortSignal): Promise<T> {
  const r = await fetch(url, { cache: 'no-store', signal: signal ?? AbortSignal.timeout(15000) })
  if (!r.ok) {
    const error = await r.json().catch(() => ({})) as { message?: string }
    throw new Error(error.message ?? (r.status === 401 ? 'Sign in again to continue.' : 'Could not load stock records. Check the backend and refresh.'))
  }
  return r.json() as Promise<T>
}
export async function stockPost<T>(url: string, payload: unknown): Promise<T> {
  const session = await stockGet<{ csrfToken: string }>('/api/auth/session')
  const response = await fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken }, body: JSON.stringify(payload), signal: AbortSignal.timeout(15000) })
  if (!response.ok) {
    const error = await response.json().catch(() => ({})) as { message?: string }
    throw new Error(error.message ?? (response.status === 403 ? 'This action requires an administrator.' : 'Save was not confirmed. Keep the entry unchanged and retry.'))
  }
  return response.json() as Promise<T>
}
export async function stockExport(url: string, filename: string) {
  const response = await fetch(url, { cache: 'no-store', signal: AbortSignal.timeout(30000) })
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { message?: string }
    throw new Error(body.message ?? 'Could not export this list. Check your connection and sign-in.')
  }
  const link = document.createElement('a')
  const objectUrl = URL.createObjectURL(await response.blob())
  link.href = objectUrl; link.download = filename; link.click()
  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000)
}
export type Receipt = {
  id: string; receiptId: string; revision: number; medicineId: string; brandName: string; manufacturer: string; baseUnit: string;
  supplier: string; deliveryReference: string; receivedDate: string; batchNumber: string; manufacturingDate: string | null; expiryDate: string;
  unitsPerStrip: number; stripsPerBox: number; unitsPerBox: number; boxesPerCarton: number;
  pieces: number; strips: number; boxes: number; cartons: number; totalUnits: number; mrpAmount: number; mrpUnit: string; mrpUnits: number;
  mrpVerified: boolean; status: string; reviewNote: string | null; correctionReason: string | null;
  createdAt: string; reviewedAt: string | null; automaticApproval: boolean;
}
export type StockMedicine = { id: string; brandName: string; manufacturer: string; dosageForm: string; baseUnit: string; batches: { id: string; batchNumber: string; manufacturingDate: string | null; expiryDate: string }[] }
export function packBreakdown(total: number, perBox: number, perStrip: number, unit: string): string {
  if (!(perBox > 0) || !Number.isSafeInteger(total)) return ''
  const boxes = Math.floor(total / perBox), remainder = total % perBox
  const strips = perStrip > 0 ? Math.floor(remainder / perStrip) : 0
  const singles = perStrip > 0 ? remainder % perStrip : remainder
  return `${boxes} boxes${perStrip > 0 ? `, ${strips} strips` : ''}, ${singles} individual ${unit.toLowerCase()}s`
}
