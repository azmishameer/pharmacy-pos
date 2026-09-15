# Operator sales counter

This milestone implements the first operator-facing sales screen. Open **Sales counter**
from the catalogue; accounts with the Operator role land here after sign-in. Admin
and Manager accounts can also use it. The charge settings milestone requires the `CartCharges` database migration, then
a backend restart and frontend refresh. Offers need `DiscountOffers`;
[cash checkout](cash-checkout.md) needs `CashCheckout`.

## Available now

- Search sellable stock by brand, generic ingredient or manufacturer.
- See medicine form, strengths, prescription/OTC classification, manufacturer, batch,
  expiry, available base units and price. Results list separately priced receiving
  lots in earliest-expiry order. Select the batch actually being supplied.
- Add individual stock units, strips when supported, or boxes. Mixed units share the
  same lot quantity limit. Adding the same lot/unit combines quantities.
- Edit quantity, remove a line or clear the cart. This is an in-memory draft; navigating
  away or signing out clears it. It is not reserved stock or a saved sale.
- A server-generated quote rechecks eligibility and quantities and reads the current
  verified lot MRP. Prices cannot be supplied or overridden by the operator.
- Show individual batch line totals, MRP subtotal, signed rounding adjustment and the
  estimated whole-taka total. Amounts below 50 paisa round down; 50 paisa or more rounds
  up. Fractions are accumulated exactly using integer rational arithmetic before the
  one final rounding, so pack selection and repeating decimals cannot change it.
- Price/line displays use up to six decimal places; these are display approximations
  for repeating fractions. The whole-taka estimate uses the exact fraction sum.

## Cash checkout

The cart quote remains a preview until **Complete cash sale** succeeds. Checkout now
saves the sale/payment/receipt snapshot and deducts stock atomically. See the
[cash checkout guide](cash-checkout.md) for cash received, change, retries, receipt
history, printing and limitations. Inventory uses remaining quantities after sales.

Search and cart prices refresh every minute; editing the cart also gets a new quote.
The old quote is hidden while loading or on errors. Quotes do not reserve stock.
Checkout revalidates eligibility, quantities and pricing; a changed quote requires
review before retrying. No statutory tax rate or private profit value is invented.

## API and verification

`GET /api/sales/stock` is paginated (20 lots), Staff-only and excludes unapproved,
inactive, expired, unverified and disposed inventory. `POST /api/sales/quote` requires
Staff authorization and CSRF, accepts only lot ID/quantity/unit inputs, and is read-only.
It sums repeated lot requests when checking available quantity. Maximum 100 lines,
1,000,000 entered packs per line and a one-trillion-BDT preview total bound protect
against pathological requests. No purchase costs or private profit data are returned.

Backend integration tests cover permissions, CSRF, sellability, expiry changes, mixed
pack calculations, multiple lot prices, duplicate-line overselling, invalid units,
server-authoritative refreshed MRP, exact fractional half-up rounding, downward
rounding and absence of inventory changes. Existing catalogue, receiving, correction
and disposal regressions are included. Frontend build/lint pass.

Browser walkthrough passed with the disposable Operator account: automatic sales-screen
entry, available-batch display, adding one 10-tablet strip plus two tablets at BDT 20
for BDT 240, editing quantity beyond availability (server error and no stale total),
and clearing the cart. The desktop two-column layout was visually inspected.

## Admin charge settings

Open **Cart charges** from the admin catalogue. Create named percentage, fixed
per-base-unit or fixed once-per-medicine charges, applying to all medicines or a
searchable selection of approved medicines. Rules start immediately, with no end
date; stop a rule to discontinue it. To change a rule, stop and replace it. Creation
and stopping preserve admin identity/time. Duplicate active names are rejected;
request IDs make unchanged retries safe. Operators cannot read settings or create/stop
rules, but receive applicable named charge amounts in server-generated cart quotes.

Percentage charges use the amount after applicable discounts and never compound
with other charges.
Per-unit fees convert packs to stock units. Once-per-medicine fees are distributed
proportionally by base-unit quantity across the medicine's lots/pack rows, and are
counted once per distinct medicine. A global rule matching a selected medicine is
counted once. Different named rules may apply together. No default rates are seeded.

The cart shows each row's MRP amount, named charges and final amount after offers,
plus a charge total and whole-taka rounding adjustment. The medicine search price
remains MRP. Quoting alone does not deduct inventory; completing cash checkout does.

`GET/POST /api/charges` and `POST /api/charges/{id}/stop` are Admin-only; writes
require CSRF. The additive migration creates `charge_rules` and `charge_medicines`.
All new routes, like the existing sales preview, are development-only.

Integration checks cover permission and CSRF enforcement, positive/two-decimal
validation, invalid medicines, concurrent retries, duplicate names, global/selected
scopes, fixed charges across mixed packs/lots, per-medicine allocation, no compounding,
stopping/replacement, rounding after charges and preserved stock/audit attribution.

Browser verification passed: admin created a selected-medicine charge, then the
Operator account added a strip and two pieces from different lots. MRP 240 plus
a once-per-medicine charge of 3 produced 243, allocated 2.50 and 0.50 across rows.
The charge settings and cart layouts were visually checked. All browser fixtures
used the disposable database; the real pharmacy database was not migrated or altered.

To enable this milestone on the development database, run:

```sh
dotnet ef database update --project backend/PharmacyPos.Api -- --environment Development
```

Then restart the API and refresh the frontend.

## Discount offers

Admin catalogue navigation includes **Discount offers**. Choose a medicine or whole
sale, percentage or fixed discount, required start and optional end. Medicine fixed
offers support per piece/strip/box with an explicit saved pack size. Whole-sale
minimum MRP subtotal defaults to zero. Stop and replace an offer to change it.

The backend selects one best monetary offer per medicine, compares that strategy
with each eligible whole-sale offer, and chooses the lowest exact final amount
after charges. Whole-sale discounts allocate proportionally by MRP, without
stacking. Fixed discounts cap at price, and final half-up rounding happens once.
Dates use Bangladesh time and include the full end date; ties prefer earlier
creation then ID, with the medicine strategy retained on a final-total tie.

`GET/POST /api/offers` and `POST /api/offers/{id}/stop` are Admin-only and writes
require CSRF. The additive `DiscountOffers` migration creates `offer_rules`.
No offers are seeded into the real database. Cart quote reads current eligible
rules, returns applied IDs/names and discounts, and still creates no sale or
stock deduction. All routes remain development-only.

Integration tests cover authorization/CSRF, retries, invalid values/dates, overlapping
offers, strip proration, inclusive end dates, indefinite/future/expired offers,
whole-sale thresholds and no stacking, manual stops, price caps, discounted
percentage charges, charge-sensitive cheapest-total selection, audit and stock.

Browser verification passed on a disposable database: admin stopped an old offer,
created an indefinite BDT 25-per-10-tablet-strip offer, and the Operator account
received BDT 12.50 off five tablets. On BDT 100 MRP, the discounted BDT 87.50 attracted
a test 10% charge of BDT 8.75; BDT 96.25 rounded down to BDT 96. No real medicines,
charges, offers or stock were changed by these checks.
