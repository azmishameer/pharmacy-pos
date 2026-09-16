# Admin sales report

Restart the backend and open **Medicine catalogue → Sales report** as an admin.
No database migration is needed. Select a date range and click **View report**.
Both dates include the full Bangladesh calendar day (UTC+06:00 / Asia/Dhaka).
The initial dates are today; up to 366 days and 50,000 sales and 50,000 refunds
can be included. Larger results are rejected with a request to shorten the period.

The report uses saved receipt pricing, sale payments and refund records. Changes
to today's MRP, offers or charges cannot rewrite historical totals. A consistent
database snapshot prevents mixing partial concurrent checkout/refund results.

- MRP subtotal, discounts, charges and signed rounding apply to sales completed in
  the selected period, before refunds. Pricing components retain stored precision.
- Sales total is the actual rounded bill total. Refunds are counted on their payment
  date, including refunds for sales before this period. Net sales = sales − refunds.
- Cash collected is cash retained after giving change, not the customer's tendered
  amount. Net cash = cash collected − cash refunded. Opening float, expenses,
  withdrawals and other drawer movements are not tracked here.
- Staff rows attribute sales to the cashier and refunds to the refund issuer.
  Staff with refunds but no sales are included; their net amount may be negative.
- This is not a profit report. Purchase cost accounting remains a separate milestone.

CSV includes the overall totals and every staff row, with date range, time zone and
generation timestamp. It is generated afresh for the displayed report dates, so new
transactions since the screen loaded may change totals. Numeric values use invariant
decimal notation, text fields are quoted and spreadsheet formula prefixes escaped.

Both JSON and CSV require admin authorization and use no-store caching. Endpoints
remain development-only. Checks cover access, exact Dhaka day boundaries, earlier-sale
refunds, cash/change, immutable breakdowns, actor attribution, empty/invalid ranges,
and CSV totals and escaping. No client data is used in integration tests.
