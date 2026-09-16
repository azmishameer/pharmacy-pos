# Charge treatment for profit reporting

Apply `ChargeProfitClassification` and restart the backend. In **Cart charges**, admins
can check **Exclude this charge from profit** when creating a charge. Checked means
money collected for the government or another party, such as VAT/tax. Unchecked means
pharmacy income. Both continue to be added to the customer's bill and printed on receipts.

Existing rules migrate with a null classification and show **Needs profit review**.
Admins must explicitly save their treatment. The saved-charge list allows reviewing
active and stopped charges, so old receipt charges can be classified as well. Corrections
require a reason, retain the initial decision and each subsequent decision, and record
admin attribution and time. Version checks reject stale edits and request IDs prevent
duplicate events on retries. Operator and anonymous requests are denied by the backend.

Classification is reporting metadata only: it never changes the charge's name, amount,
scope or active status, nor rewrites saved receipts. Future profit reports should join
saved charge IDs to their latest reviewed treatment, including past sales. They must
mark unresolved classifications as incomplete rather than assume they are income.

The [profit report](profit-reports.md) uses this classification and review. Existing
sales reports continue to show all charges and actual customer payments. The
profit report uses corrected purchase costs, identifies missing costs, reduces income
for refunds and reverses returned-stock cost only upon admin approval for resale.
