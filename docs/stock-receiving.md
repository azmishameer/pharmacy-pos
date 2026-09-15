# Stock receiving, expiry and disposal

This development milestone adds a working receiving and inventory preview. It has
not yet been migrated into the user's pharmacy database. No real pharmacy records
were inserted, approved or disposed during verification.

## Setup

Apply the migration from the project root, then restart the backend:

```sh
dotnet ef database update --project backend/PharmacyPos.Api -- --environment Development
```

The migration `StockReceivingAndDisposal` adds receiving lines and revisions,
receiving movements, review/export audit events and disposal records. It preserves
existing catalogue and batch records. Downgrade removes the new stock tables and
is not a data-preserving rollback after receiving starts.

## Receiving

Find an approved medicine in the catalogue and select **Receive stock**. Record
supplier, optional delivery reference, received date, batch number, manufacturing
date if known, and expiry. A saved catalogue batch can be copied into the form.
Dates conflicting with an existing batch block approval rather than overwrite it.

Enter packaging from the label. Tablets/capsules can use strips (units per strip,
strips per box); otherwise enter base units per box. Carton entries require boxes
per carton. Enter separate cartons, boxes, loose strips and individual units without
counting a pack's contents twice. Quantities are whole numbers. Every receipt saves
its original counts and conversion snapshot. The server checks the total independently
of the browser. Preview shows total units, equivalent boxes/strips/singles, and an
estimated MRP value. It is not a purchase-cost estimate or a checkout price.

Record the printed MRP per individual unit, strip or box. Preserve the decimal amount
(up to 2 decimal places) and integer denominator; a rounded per-tablet rate is not
the authoritative price. Intermediate display estimates use 2 decimals. Checkout,
offers, taxes and final whole-taka rounding are not implemented by this milestone.

Each form save is one independently reviewed receiving line. Lines can share a
delivery reference; multi-line receipt grouping and bulk entry UI are future work.
Repeated requests with the same save ID and unchanged payload do not receive twice.
A new delivery of the same batch is allowed, using a separate receiving lot. Different
printed MRPs or packaging on those lots never change older received quantities/prices.
Standalone official MRP revisions for existing stock remain a separate planned step.

## Approval and corrections

Operators/Managers submit pending entries; they can inspect their own entries and
history. Admin-created entries approve stock immediately. Checking **I checked this
printed MRP against the packaging** explicitly verifies price; stock approval alone
never verifies it. Missing verified MRP means zero sellable quantity. Verification
can also be completed later from **Receiving & review → MRP verification pending**.
Purchase costs are not collected in this step and must not be inferred as zero.

Admins review and approve one entry at a time, or return it with a reason. Pending
stock stays outside inventory and does not reduce older approved stock. The admin
can export all pending entries as CSV before approving; this exports current revision
IDs, quantities and conversions and does not change approval state. Formula-like
text is neutralized and CSV cells escaped. Each export is audited.

Staff may correct their own unapproved entries, supplying a reason. An admin can
correct/re-enter any unapproved entry; that correction is automatically approved.
The original is superseded, not duplicated as a second line. The same receipt identity
persists across revisions, with reviewer actions retained in a separate history.
Old browser/CSV revision IDs cannot approve superseded data. Already approved stock
cannot be edited through receiving; future adjustments need their own workflow.

Creation, review, revision changes and movement posting are transactional with a
consistent PostgreSQL advisory lock order. A unique movement source per receiving
line prevents double posting, including competing approval retries. A failed batch
validation rolls back the entire operation. A receiving lot uses its exact approved
revision; price verification never alters quantity.

## Expiry and physical disposal

Confirmed by the user: record expired deliveries as unsellable stock, and apply the
same handling to stock that expires on the shelf. The preview interprets full dates
in the shop timezone Asia/Dhaka. Stock remains within date through its printed expiry
date and is expired from the following local day. The server recomputes eligibility
on each inventory request, and an open inventory page refreshes every minute. There
is no reliance on a user editing stock or a background expiry migration. Month-only
expiry labels are not supported yet; do not invent a specific day.

Inventory separates **All approved stock**, **Sellable stock**, and **Unsellable stock**.
Expired stock remains counted physically on hand but has zero sellable quantity.
Inactive/unapproved medicines and unverified MRPs also block sellability. Disposal
through this milestone is restricted to expired stock, not merely an unverified price.

After actual physical disposal, an admin chooses **Record physical disposal**, enters
a reason/reference, and confirms the entire displayed receiving lot was disposed.
This milestone disposes the whole remaining lot; partial disposal is not implemented.
The server verifies expiry again and records one outgoing disposal quantity atomically.
Retries cannot dispose twice; operators cannot perform this action. The lot disappears
from inventory, while the original receipt/movement and disposal audit remain intact.

**Disposal history** and **Export disposal CSV** are admin-only. They show records
for exactly three calendar months from the recorded disposal timestamp (`AddMonths(3)`),
then automatically omit them. This retention applies to the operational list/export;
underlying receipt and audit records are retained to preserve quantity history. No
background hard deletion erases why stock left inventory. Timestamps are stored in UTC.

## Technical limits and verification

All endpoints are development-only and require staff authorization. Writes require
CSRF; approval, MRP verification, disposal and exports enforce Admin authorization on
the server. Public response projections do not serialize entities or private costs.
Lists paginate 25 items; exports fail clearly above 10,000 items instead of silently
truncating. Inventory is derived from receiving movements minus disposed lots; there
is no mutable second balance that could drift. Sales, returns, quarantine and partial
adjustments must extend this ledger and revalidate eligibility at transaction time
before a usable checkout is introduced.

Meaningful PostgreSQL integration checks passed for mixed conversions, input limits,
CSRF and roles, pending invisibility, partial approval, explicit MRP verification,
price snapshots, same-batch conflicts, retries, concurrent approval/disposal, immutable
corrections and stale revisions, audit metadata, CSV escaping, expired deliveries and
current stock, physical disposal, three-month list/export retention. Existing sign-in
and catalogue regression checks pass. Frontend build and lint pass.

Browser verification passed using a disposable admin account: received an expired
mixed-pack delivery (2 cartons × 20 boxes × 30 tablets + 1 box + 2 strips + 5 tablets
= 1,255), verified its recorded MRP, confirmed automatic approval with zero sellable
stock, recorded physical disposal and checked that the inventory lot disappeared.

## Correct an approved MRP entry

Admins can now select **Correct MRP** on an approved entry in **Receiving & review**.
This corrects a recorded amount/unit mistake, such as 600 per tablet instead of 600
per box. It requires a reason and explicit confirmation against the packaging. The
form shows the resulting per-unit rate before saving. New receiving forms also
require an explicit MRP unit choice instead of defaulting to an individual unit.

The correction changes only the approved lot's current recorded MRP and verification
metadata. It does not post a movement, add stock, change pack conversions or alter
any other lot's price. Before/after amounts, units and denominators, reason, admin
and time are retained as an `MrpCorrected` stock review event, visible under **View
entry history → MRP correction history**. Existing receiving/approval history stays
intact; the original price can be recovered from that correction audit. There is no
new database migration. A conditional current-price check prevents stale overwrites,
and an event request ID makes unchanged retries idempotent. Disposed and unapproved
lots cannot be corrected here. This is an entry-error correction, not an implementation
of official effective-dated price revisions or historical sale repricing.

Backend checks cover role/CSRF protection, explicit verification, stale/repeated
requests, saved before/after history and unchanged stock quantities/movement count.

With the `CashCheckout` migration applied, inventory and counter quantities subtract
completed sale movements. Receiving history keeps the original delivered amount.
Expired disposal removes only the remaining unsold units.
