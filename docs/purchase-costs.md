# Admin purchase costs

Apply the `PurchaseCostHistory` migration and restart the backend. Sign in as admin
and open **Medicine catalogue → Purchase costs**. Search by medicine name or batch.

Costs belong to individual approved receiving records, not the medicine catalogue.
Enter the total amount paid for the entire original delivery in BDT, after supplier
discounts, with up to two decimal places. For example, a delivery of 600 tablets
costing ৳9,000 has a unit cost of ৳15. The original quantity is used even if some
units have already been sold, returned or disposed. Unit cost is displayed to six
decimal places; the saved total and original quantity remain the authoritative values.

Missing cost displays **Not entered**, never zero. Zero-cost stock can be explicitly
recorded with the confirmation checkbox and a note. Purchase cost is independent of
stock approval, MRP, offers, sales charges and eligibility to sell. Entering or correcting
cost does not update any of those values or rewrite existing receipts.

Admin corrections require a reason and append a new revision. Earlier amounts,
admin attribution and timestamps remain in paginated history. Concurrent changes
reject stale revisions; identical retry requests return the original saved result.
Sold-out and disposed deliveries remain available for late cost entry. Pending or
rejected entries must be approved before receiving a purchase cost.

All cost list, history and write endpoints enforce admin authorization. Writes also
require CSRF protection. Operator stock and checkout responses have explicit public
projections and contain no purchase costs. The private data lives in a separate table
with no navigation exposed by public receipt DTOs. No cost export or profit report is
included in this milestone, and historical cost-of-goods accounting remains future work.

Integration checks use a disposable database and cover authorization, no leakage into
operator responses, validation, concurrent retry safety, stale corrections, audit history,
missing versus zero costs, sales before cost entry, and unchanged stock/selling prices.
