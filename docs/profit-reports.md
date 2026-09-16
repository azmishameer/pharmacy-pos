# Admin profit report

Restart the backend and open **Medicine catalogue → Profit report** as admin.
No new migration is required beyond `PurchaseCostHistory` and `ChargeProfitClassification`.
Select up to 366 inclusive Bangladesh calendar days. JSON and CSV are admin-only,
no-store, and calculated from one consistent database snapshot.

## Calculation

- Customer payments use the original rounded sale totals, already net of discounts.
- Each immutable receipt line has the same paid share used for whole-line refunds.
  Excluded charges receive `paid line share × excluded charges / original final line
  price`. This distributes bill rounding proportionally between income and excluded
  charges and lets a complete refund reverse exactly that line's income/exclusion.
  Zero-paid lines carry zero income and zero exclusion.
- Net income = payments − excluded charges collected − refunds + excluded charges
  refunded. Unchecked charges count as pharmacy income. Latest reviewed charge
  classifications apply retrospectively, including stopped charges.
- Cost of sales = quantity sold from each delivery × latest delivery purchase total /
  original received quantity. Unit costs are not rounded before calculation.
- Cost recovered = returned quantities approved for resale in the period × that same
  latest delivery unit cost. Held and disposed returns recover nothing. There is no
  second expense for disposing a held return: its sale cost was never reversed.
- Gross profit = net income − cost of sales + recovered cost.

Sales use completion dates; refunds use issue dates; cost recovery uses resale approval
dates. An earlier-sale refund or earlier-return approval can therefore affect a period
with no sales. Latest cost corrections update earlier reports, as requested. Display and
CSV round computed values to six decimals; small displayed-component differences of
one millionth of a taka are possible because totals are calculated before display rounding.

## Missing information

Missing purchase costs and unclassified charges are listed by delivery/charge reference.
Affected totals and gross profit are null, rendered as **Incomplete**, and blank in CSV.
They are never treated as zero. Explicit zero-cost deliveries are valid. Only charges on
relevant paid sale/refund lines affect completeness. An empty period is complete with
zero totals. Reports exceeding 50,000 sales, refund items or resale approvals are rejected
with an instruction to select a shorter period.

Export recalculates the selected dates using current costs/classifications and includes
status, totals, unresolved references and calculation notes. It can differ from a previously
loaded screen after new activity or corrections; the generation timestamp identifies it.

This is gross profit from sales and returns, before rent, salaries and other operating
expenses. Unsold inventory write-offs are not included. It is not a cash balance, tax
liability calculation or statutory accounting report.

Integration checks cover excluded tax versus income charges, receipt rounding, refund
and approval timing, disposed returns, retroactive cost corrections, missing costs,
unclassified historical charges, authorization, empty periods and CSV missing values.
