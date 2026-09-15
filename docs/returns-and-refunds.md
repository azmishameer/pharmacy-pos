# Returns and refunds

Apply `ReturnsAndRefunds`, restart the backend and refresh the frontend. Open
**Sales counter → Returns & refunds** as an Admin or Operator.

## Complete receipt items

Find the original POS receipt number, then select entire receipt rows. Quantities
cannot be edited: two strips means both strips, three individual tablets means all
three. Other receipt rows may remain with the customer. Each row can be returned
only once; different rows may be returned on different days. The server accepts
row indexes only and derives all quantities from the original immutable receipt.

Admins and operators can record a return and cash refund immediately, with no
approval. Staff confirm full receipt of the items, the cash refund and a reason.
The record preserves original sale ID, row indexes, sold pack quantities, base units,
refund amount, method, staff identity/name and timestamp. Return history is available
to admins and operators, including returns processed by other cashiers. Exact receipt
lookup can find sales completed by another cashier without broadening ordinary
sales-history permissions. Manager-only accounts do not process returns.

## Refund amount

The original receipt's final line amounts already include its discount and charges.
Allocate the original paid bill total in paisa proportionally across those immutable
line amounts: floor each share, then distribute remaining paisa by largest remainder,
with receipt order breaking ties. Zero-value lines receive no share. This also allocates
the original whole-taka rounding adjustment; all complete-line refunds add up to
exactly the original paid total, independent of return order. Partial refunds may
include paisa and are not rounded again. Current MRP, offers and charges are ignored.

Legacy/current sale snapshots store final line amounts to six decimal places; those
saved amounts are the allocation weights. Original checkout still rounds using exact
fractions. The allocation never changes the original sale or recalculates its prices.

## Separate inventory

Returned items start as **Held**, excluded from normal stock and counter availability.
Only an admin can inspect and approve an entire held item for resale, entering a reason
and explicit confirmation. Restocking credits its original receiving lot once. Current
expiry, active/approved medicine, approved receipt, verified MRP and disposal status
are checked again. Expired or disposed lots cannot be restored to sellable stock.

An admin can instead record physical disposal of held goods. Disposed returned items
remain in the disposal list and admin CSV export for three calendar months. Permanent
return/refund audit history remains. Once restocked, the units participate in normal
sales/expiry/disposal; the original receiving quantity remains unchanged.

The stock balance is original receipts + approved returns − sales. Disposed main lots
remain excluded. Held/disposed returned items never credit normal stock. Each item
has one terminal decision (Restocked or Disposed), with admin identity/time/reason.

## Safety and retry behavior

A unique return request ID is stored in browser session storage before posting. Reloading
the returns workspace or retrying after a lost response uses the same ID. Staff must
not give cash again on retry. The return/refund and held items commit together under
the stock transaction lock. A unique sale/row constraint prevents duplicate refunds;
identical retries return the original result. Admin decisions share the catalogue/stock
lock order with checkout and restock once even on retry. No physical payment service
is called: cash-refund records reflect staff confirmation of manually issued cash.

## Endpoints and checks

- `GET /api/returns/sale?number=POS-00000001`: receipt lookup and refund shares.
- `POST /api/returns`: complete-row return and cash refund; Admin/Operator + CSRF.
- `GET /api/returns`: paginated audit history.
- `GET /api/returns/stock?status=Held`: held, restocked or recent disposed items.
- `POST /api/returns/stock/{id}/review`: Admin + CSRF, inspection/restock or disposal.
- `GET /api/returns/disposals/export`: Admin-only, escaped CSV, export audit event.

Integration tests cover original discount/charge/rounding allocation, full quantities,
invalid/duplicate selection, concurrent identical retries, cross-cashier returns,
role and CSRF boundaries, held isolation, single restock credit, original-price refunds,
expiry blocking, main/return disposal quantities, three-month retention and audit.
All existing checkout, inventory, pricing and authentication tests pass.

No unreceipted returns, partial-row returns, refunds without returned goods, exchanges,
non-cash refunds or reversal of a completed return are included in this milestone.
All routes remain development-only, consistent with the existing application.

Browser verification passed on disposable records: an Operator returned the complete
five-tablet item on POS-00000001 for BDT 95. The audit showed the operator and reason,
and Held inventory had no approval controls. An Admin then recorded inspection and
approved it; the item left Held stock and the counter showed five available tablets
in its original previously sold-out batch. No real pharmacy data was modified.
