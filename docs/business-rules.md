# Pharmacy business rules

These requirements were agreed during project planning. They describe intended
behavior; they are not yet implemented. The current application only verifies
frontend, backend, and database connectivity.

## Medicine catalogue and packaging

- Store brand, generic ingredients, strength, dosage form, manufacturer, and
  OTC/prescription classification separately from stock batches.
- The user's example is Jardimet tablet, Empagliflozin + Metformin Hydrochloride,
  5 mg + 500 mg, Beximco Pharmaceuticals Ltd., classified by the user as prescription.
  This is supplied catalogue data, not independently verified clinical information.
- For this example, 1 strip = 10 tablets and 1 box = 3 strips = 30 tablets.
- Count tablet stock internally in individual tablets. Display equivalent pack
  quantities without counting the same stock more than once.
- Support receiving by tablet, strip, box, carton, or mixed quantities.
- Carton size is not yet known. At receipt, the operator can enter boxes per carton
  from its label. Save that conversion with the receipt so later changes do not
  change historical quantities.
- Configure packaging per medicine. Rules for non-tablet products remain to be designed.

## Selling prices and discounts

- Derive strip and box selling prices from the single-tablet price for now.
  The example price of BDT 20 per tablet gives BDT 200 per strip and BDT 600 per box.
- Purchase cost and selling price are separate values. Selling value cannot be
  used to infer purchase cost.
- Per the user's business requirement, MRP is government-set, not a discretionary
  price proposed by staff. The operator records the official MRP for a new medicine;
  admin review verifies the accuracy of that entry rather than authorizing a different MRP.
- The verified MRP is the normal selling price before discounts from admin-configured offers.
  Do not introduce an independently chosen regular selling price.
- Confirmed: batches of the same medicine can have different printed MRPs. Store
  verified MRP against the batch, not as one price that overwrites every batch of
  that medicine. Receiving a newer batch must not change an older batch's price.
- Checkout uses the verified MRP of the batch actually supplied. If a sale draws
  from differently priced batches, preserve each batch's quantity and price in the
  sale details and show the price breakdown on the receipt, rather than substituting
  the newest price or averaging prices. Apply offers and final rounding as above.
- Operators can submit recorded updates to reflect officially revised MRPs, also
  requiring admin verification. Store pending entries and active verified prices
  separately; an unverified update must not silently replace the active price.
- Show current verified and newly recorded MRPs during review. Preserve who entered
  and verified each value, when it occurred, and the values before and after verification.
- Price approval and receiving-line approval are separate decisions. A batch
  without a verified MRP cannot be checked out; approving physical stock
  must not silently verify a recorded MRP. The review UI should make both states clear.
- Staff can enter the printed MRP per tablet, strip, or box. Preserve the entered
  amount, selected unit, and the batch's applicable packaging conversion with the
  price record; calculate the per-tablet MRP by dividing by tablets in that unit.
  For example, BDT 600 per box / 30 tablets = BDT 20 per tablet. Later packaging
  changes must not reinterpret historical price entries. Operator-entered prices
  still require admin verification.
- Treatment of official revisions and effective dates for existing batches remains
  to be confirmed before implementing price updates.
- Administrators configure current offers for particular medicines in advance.
  Only administrators may create, change, or disable these discount offers; operators
  and managers do not receive this permission by default.
- Each offer has a required start date and an optional end date. It becomes eligible
  from its start date; if an end date is present, it expires automatically. Without
  an end date, it continues indefinitely until an admin manually stops it.
- Manual stopping overrides the date schedule. Record who stopped the offer and when;
  retain its history and previously discounted sales. An indefinite offer must use
  a missing end date, not an invented far-future date.
- When multiple active, eligible offers apply to the same medicine, automatically
  select the single offer producing the largest monetary discount for the purchased
  quantity. Do not combine overlapping medicine offers. Compare the actual calculated
  discounts, not percentage and fixed-amount labels.
- At operator checkout, the backend determines eligible offers and calculates the
  final sale price automatically from verified MRP and applicable offer rules.
  The operator can complete an eligible discounted sale without an admin approving
  or manually discounting that individual customer's transaction. Operators cannot
  enter arbitrary discounts, change offer rules, or override calculated prices.
- Support percentage or fixed BDT offer discounts. Earlier planning also requested
  discounts at both medicine-line and whole-sale level; medicine offers now have a
  confirmed automatic workflow, while automatic whole-sale eligibility remains to
  be defined. Manual per-customer admin discounting is not required.
- For a fixed-amount medicine offer, the admin selects the discount unit: per piece
  (tablet for the example medicine), per strip, or per box. Store the amount and
  selected unit explicitly rather than assuming every fixed amount is per tablet.
  Preserve the relevant packaging conversion with the offer revision.
- Strip/box fixed discounts are prorated for smaller quantities. For example,
  BDT 15 off a 10-tablet strip gives BDT 7.50 off 5 tablets. Calculate using base-unit
  quantities and the offer's saved conversion so equivalent quantities receive the
  same discount regardless of the selling unit selected.
- No stacking between medicine offers and whole-sale discounts. Calculate two
  alternative totals from the same undiscounted sale: one using the best eligible
  offer for each medicine, and one using the best single eligible whole-sale offer
  without medicine discounts. Apply whichever yields the lower total. Do not apply
  a whole-sale discount to an already-discounted subtotal or combine whole-sale offers.
- Round the final payable sale total to the nearest whole BDT, with halves rounded up,
  once at checkout, rather than rounding each tablet, line, or discount separately.
  BDT 92.49 becomes BDT 92; BDT 92.50 becomes BDT 93; BDT 92.51 becomes BDT 93;
  BDT 92 stays BDT 92. This replaces the earlier, incorrect always-round-up rule.
  Select the best eligible discount alternative using its unrounded total first.
- Show and preserve the pre-rounding total, a separate signed rounding adjustment
  labeled "Rounding adjustment" (identifying rounded up or down), and the final payable amount on the
  receipt. Example: BDT 100 less BDT 7.50 discount = BDT 92.50, plus BDT 0.50
  rounding = BDT 93 payable. For BDT 92.49, show an adjustment of -BDT 0.49 and
  BDT 92 payable. Do not alter the recorded discount to hide rounding.
- Confirm discount limits, calculation precision, allocation of discounts and rounding
  for refunds, and tax handling before implementing checkout calculations.
- Preserve original MRP, applied offer identity/version, discount amounts, and final
  prices in sale history and receipts so later offer changes do not alter past sales.
- Audit the admin who configures or changes each offer and the operator who completes
  the sale. Record automatic discount application without falsely attributing a manual
  approval to an admin. Revalidate eligibility on the server when committing checkout.

## Purchase costs and stock availability

- Only administrators may enter, change, or view purchase costs and view profit reports.
- Operators can receive quantities and record packaging, supplier, batch, and expiry
  information without access to purchase costs.
- Accepted: only admin-approved stock may become available for sale. Approval may
  occur before an administrator enters its purchase cost. Mark the missing cost
  as **Cost pending**; approval and cost completion are independent states.
- Cost pending does not bypass other stock rules: expired or quarantined stock
  remains unavailable, and sales cannot exceed available quantities.
- Represent missing costs explicitly, not as zero. Do not require cost entry from
  an operator to finish receiving otherwise valid stock.
- Profit reports must flag sales with missing costs as incomplete; they must not
  present revenue from those sales as fully earned gross profit.
- Enforce restrictions in the API, including excluding cost fields from operator
  responses and exports. Hiding fields in the browser is insufficient.
- Admin cost entry and correction must be audited. Decide cost allocation for
  separate receipts of the same batch and treatment of later-entered costs in
  historical reports before implementing profit calculations.

## Required admin approval of new stock

- Operator-entered receipts remain **Pending approval** until an administrator
  explicitly approves them. Entering a receipt alone must not increase available stock.
- Admin-created entries are automatically approved and posted on valid submission;
  they require no separate approval action or second administrator. Record the
  submitting admin as the author and approving actor, with automatic approval noted.
- Administrators can view pending receipts and export the new-stock details as CSV
  for checking before approval. Exporting a CSV does not approve or post stock.
- Approval is per receiving line, not all-or-nothing for a receipt. An admin can
  approve correct entries while other entries in the same submission stay pending.
  Each line identifies one medicine and batch, with its quantities and conversions.
  Show a receipt as partially approved when it contains approved and pending lines.
- An admin can return an incorrect line to the operator with a reason. The operator
  can correct it and resubmit it for approval; it remains unavailable until approved.
- An admin can also correct or re-enter the item themselves. Preserve the original
  submission and record the editor, correction reason, timestamp, and revised values.
  Link a replacement to the original and mark the original superseded so both cannot
  later be approved as separate stock receipts. These rules concern unapproved entries;
  corrections to already-approved stock require the separate adjustment workflow.
- An admin's submitted correction or re-entry of an unapproved item is likewise
  automatically approved. Tie approval to that revised entry, supersede the original,
  and post stock once, atomically. Operator corrections still require admin approval.
- Only after approval may the newly received quantity appear in normal inventory
  and POS availability. Pending receipts must still be visible in the admin review
  workflow. Operator access to their pending submissions remains to be decided.
- Pending quantities must not affect existing approved stock of the same medicine
  or batch: existing stock remains visible and sellable under the normal rules.
- Approval is enforced by the backend. For each approved line, post its receiving
  stock movement and mark that line approved together in one database transaction,
  recording approver, approval time, and the exact line revision approved.
- Repeated clicks or retries must not post stock twice. Operator changes after export
  or review require fresh admin review of the changed revision. Admin-submitted changes
  follow the automatic approval rule and cannot approve an older revision by accident.
- Approved quantities and conversions must not be silently edited. Subsequent
  corrections require a traceable workflow, whose approval rules remain to be defined.
- CSV exports should identify receipts and revisions and include medicine, supplier,
  batch, expiry, entered pack quantities, conversion factors, and total base units.
  Cost information, if included, remains admin-only; CSV text must be escaped to
  prevent spreadsheet formula execution.
- Approval does not bypass expiry, quarantine, or overselling checks. Cost can
  remain pending after approval, with incomplete profit reporting as described above.

## Remaining decisions for the first receiving workflow

- Exact date-boundary/time-zone semantics, any additional qualifying quantities,
  ties between equally beneficial offers, and automatic whole-sale
  discount eligibility. These must be settled before implementing checkout pricing.

- How official MRP revisions apply to existing stock and effective dates.
- Whether operators may view their own pending receipt submissions.
- How the admin enters purchase amounts (per tablet, selected pack, or receipt total).
- Whether mixed packs of one medicine can appear together on a receipt line.
- Validation and correction of operator-entered carton conversions.
- Country/tax and receipt requirements; BDT prices have been supplied as examples.
- Exact expiry-date interpretation and internal pricing precision (including repeating
  fractional results); the final payable nearest-whole-taka, half-up rule is confirmed above.

Keep medicine details, physical stock movements, and purchase costs distinct in
the database design so permissions and missing costs do not prevent accurate
quantity tracking.

## Expiry and disposal clarification (September 15, 2026)

- Both expired deliveries and stock expiring on the shelf must be recorded as
  unsellable stock; expired deliveries are not rejected merely for being expired.
- Admins may remove stock from inventory after physical disposal.
- Disposal records remain in the operational list for three months, with admin CSV
  export available during that period. Receiving/audit history must preserve the
  reason stock left inventory.
- The implemented receiving/expiry/disposal milestone and its current limits are in
  [stock receiving](stock-receiving.md). Earlier sections describe the full planned system.

## Cart charges clarification (September 15, 2026)

- Admins configure named charges such as VAT, tax, or other
  charges. Operators cannot create or change these rules or override cart charges.
- A charge can apply to all medicines or selected medicines. The same charge must
  be counted only once if a medicine matches both scopes.
- Catalogue medicine prices continue to show printed MRP without these additions.
  The cart shows each medicine's discount, applicable named charges, and final price;
  the receipt must also itemize these amounts.
- Percentage and fixed-amount charge options are available in the cart preview. For fixed charges, the
  admin chooses per individual base unit or once per medicine in the cart.
  Per-unit charges scale with total base units after pack conversion. Once-per-medicine
  charges apply once across that medicine's cart rows, including different packs or lots.
- No statutory rates have been specified or seeded. Charge settings and cart
  calculations and offers are implemented; checkout and printed receipts remain pending.
- Charges start immediately and continue until stopped. To amend a rule, stop it
  and create its replacement; creation and stopping retain admin attribution.
- Percentage charges do not compound with other charges. The preview calculates
  them on the discounted medicine amount before final rounding.
- Once-per-medicine amounts are allocated across that medicine's rows in proportion
  to base-unit quantities. Exact fractions are retained until final whole-taka rounding.

## Offer preview implementation (September 15, 2026)

- Admins create medicine or whole-sale offers and stop them. Changes use stop and
  replace, preserving the old offer and actor/time. Operators cannot override offers.
- Start/end dates use Asia/Dhaka calendar dates, inclusive. An absent end date is
  indefinite; stopping takes precedence. Equal medicine discounts use earliest
  creation time, then stable offer ID. An exact alternative-total tie keeps the
  medicine strategy. These deterministic defaults do not alter customer totals.
- Whole-sale minimum MRP subtotal is admin-configurable, zero for all bills. This
  is the implementation default pending the user's optional eligibility preference.
  Eligibility uses the original subtotal before discounts and charges.
- Fixed medicine discounts store admin-entered piece/strip/box conversion and
  prorate by individual quantity. Discounts are capped at each line's MRP amount.
- Whole-sale discounts are capped at the subtotal and allocated across lines
  proportionally by original MRP, using exact fractions. Only the cheapest final
  unrounded strategy, including charges, applies. No medicine/whole-sale stacking.
- Cart responses include applied offer IDs and names, discounts, charges and final
  row prices. Persisted sale/receipt snapshots and refund allocation remain pending
  for checkout; no stock deductions or completed-sale records are created yet.

## Cash checkout implementation (September 16, 2026)

- Cash checkout is implemented with received amount, change, atomic stock deductions
  and saved receipts. Card, bKash and Nagad remain future payment methods.
- Payments have separate records so future methods can extend the sales model.
- Unique checkout IDs make identical retries return the same completed receipt.
- Inventory and disposal use remaining stock; original receiving history is preserved.
- Receipts snapshot prices/offers/charges and rounding at completion. Later rule
  changes do not recalculate them. Admins can view all receipts; other staff view own.
- Returns/refunds and prescription-record controls remain to be designed. See
  [cash checkout](cash-checkout.md) for implementation limits.

## Returns and refunds (September 16, 2026)

- Admins and operators may process returns/refunds without approval, with original
  receipt reference, item quantities, refund amount, reason, staff identity and time.
- Only complete receipt rows can be returned: two purchased strips means both,
  three individual tablets means all three. Different receipt rows may be retained.
- Refunds use original paid amounts, including discounts, charges and allocated
  rounding; returning every row refunds exactly the original bill total. See
  [allocation details](returns-and-refunds.md) for deterministic paisa allocation.
- Returned goods remain separate and unsellable. Only admin inspection/approval can
  restock a full returned item. Expired goods cannot become sellable. Held goods may
  be disposed with an admin audit record and three-month disposal CSV availability.
