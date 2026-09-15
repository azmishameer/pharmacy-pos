# Cash checkout

Apply the additive `CashCheckout` migration, restart the API, and refresh the frontend.
The Sales counter now supports cash payments, completed sales, stock deductions and
saved receipts. All endpoints remain development-only, matching the existing app.

## Operator workflow

1. Add the actual batches and quantities being supplied.
2. Check the MRP, selected offer, charges and final whole-taka total.
3. Enter cash received, confirm receipt of cash and items, then Complete cash sale.
4. Read the saved receipt and change due. Print receipt opens the browser print dialog.
5. Start new sale clears the completed checkout. Recent sales lets staff re-open and
   reprint their own receipts; admins can view all receipts. Printing never posts a sale.

The sale confirmation stores the pending request in browser session storage, keyed by
username, before sending it. Retrying or reloading the same tab reuses its identifier.
A lost response does not justify creating a new sale or collecting cash again. The
pending screen retries the original request to recover the receipt. Session storage
is not an offline queue and does not survive every browser/session clearing action;
Recent sales provides access to server-confirmed receipts after it is lost.

## Data and consistency

- `sales` stores a unique checkout request ID, sequential receipt number, operator
  identity/name, time, payable amount, request fingerprint and immutable JSON price
  snapshot. Receipt numbers may have gaps after failed transactions; uniqueness is
  enforced. Receipts use BDT and Bangladesh time.
- `sale_payments` is separate from the sale, with method, amount applied, tendered cash
  and change. Only Cash is currently accepted. Card, bKash and Nagad workflows can be
  added later without replacing the sale model; no payment integration is implied.
- `sale_stock_movements` records the outgoing quantity for each receiving lot. Original
  receipt quantities remain untouched. Inventory exposes both original received units
  and current on-hand units; counter availability and expired disposal use the latter.
- Checkout takes the catalogue, stock, charge and offer transaction locks in that order.
  It checks the request ID first, then reuses the server pricing/eligibility calculation.
  The quote fingerprint includes stock availability and the full displayed pricing;
  changed quotes return a conflict requiring a refreshed review. Expired, unapproved,
  unverified, disposed or insufficient stock cannot complete a sale.
- Sale, payment and deductions commit together. The unique request ID and fingerprint
  return the original receipt on an identical retry, even after subsequent pricing
  changes. A changed payload or another operator cannot reuse that ID.
- Cash must cover the rounded payable amount and have at most two decimal places.
  Zero-payable sales accept zero cash; over-discounts cannot produce negative prices.
- Snapshot prices, discounts, named charges and rounding do not change when admins
  later change current rules or MRP. No private purchase-cost data is exposed.

## API

- `POST /api/sales/checkout`: Staff + CSRF; request ID, lot/quantity/unit lines,
  quote hash and cash received. The client cannot set the final total or payment method.
- `GET /api/sales/receipts/{id}`: Staff; own receipt or Admin.
- `GET /api/sales/receipts?page=1`: paginated own history, all history for Admin.
- `POST /api/sales/quote` remains read-only and now includes `quoteHash`.

## Verification and limits

Integration tests cover cash/precision validation, CSRF and role boundaries, saved
change and receipt breakdown, simultaneous identical retries, immutable snapshots,
competing checkouts for the last units, stale prices, expiry after quoting, unchanged
receiving history, remaining inventory and disposal of only the unsold remainder.
Frontend build and lint pass. Real pharmacy records are not used for these tests.

[Complete-item returns and refunds](returns-and-refunds.md) are now supported with
the `ReturnsAndRefunds` migration. Voids, split payments, non-cash payments, cash drawer accounting,
printer-specific thermal layouts, pharmacy branding and prescription-record handling
remain outside this milestone. Receipts currently use the generic Pharmacy POS heading.

Browser walkthrough passed using a disposable Operator account: five tablets at
BDT 20 with a 10% offer produced BDT 90 payable; BDT 100 cash yielded BDT 10 change.
Receipt POS-00000005 was visually inspected. Reloading recovered that same receipt,
then Start new sale showed 652 units remaining from 657. Recent sales included the
receipt. Physical printer output was not tested; Print receipt invokes browser printing.
