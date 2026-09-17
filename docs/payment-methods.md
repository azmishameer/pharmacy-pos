# Cash, card, bKash and Nagad

The Sales counter supports one method or a split across up to four methods, with each method used once per sale. Amounts must cover the final rounded bill exactly. Zero-price sales use a single zero-amount cash payment.

## Checkout

1. Review the cart and final amount.
2. Select a payment method and enter its amount applied to the bill. For a split, reduce the first amount and choose **Add another payment method**; the next row is prefilled with the remaining amount.
3. For cash, enter cash received. Change is calculated only against the cash portion.
4. For Card, bKash and Nagad, complete payment on the terminal or provider's merchant service and enter the successful transaction reference. Confirm the payments and select **Complete sale**.

This feature records externally completed payments. It does not contact a payment gateway, charge a card, query a wallet or automatically send money. A terminal/merchant account must independently confirm success. Do not enter card numbers, PINs or CVVs. Use the full terminal transaction reference; if a terminal reuses short receipt numbers, include its terminal/date information to make the reference unique.

References are trimmed and compared case-insensitively within each payment method. A reference already used for a sale cannot be used for another sale, even if that sale is later refunded. Correct repeated submissions retry the same checkout ID and recover the saved receipt. Do not take another payment after a lost response. If the quote changes or checkout is rejected, reconcile any money already collected before changing the cart or collecting more.

The backend recalculates the bill, validates every amount and reference, and commits all payment records, the receipt and stock deduction in one transaction. Historical cash checkout retry hashes remain compatible. Receipts and reprints show each payment amount and its reference, plus cash tendered/change where applicable. The staff member and completion time remain part of the sale audit.

## Refunds

Existing requirements remain: original receipt, no more than 15 elapsed days after purchase, complete receipt lines, no approval needed to record a refund, and returned stock held until admin resale approval.

The operator selects:

- **Original payment methods:** the selected items' refund is split among the original methods. The form shows each amount and requires a completed refund reference for each non-cash method.
- **Cash:** the whole selected refund is issued in cash, regardless of the original sale methods.

The total refund always comes from the original receipt's prices, discounts, charges and allocated rounding. For original-method refunds, cents are allocated in receipt-line order against remaining original payment balances using largest remainders. Both each line's refund and each original method's total are preserved exactly; returning lines in a different order does not change their shares. Choosing a cash refund explicitly redirects that item's original payment shares to cash.

Complete refunds outside the POS, confirm them, then record the return. The audit stores destination, actual method amounts, non-cash references, staff member, reason, time and receipt confirmation. Retry the same return reference after an uncertain response; do not issue money again. Refund records and held inventory commit together.

## Reports

Admin sales reports and CSV exports include collected, refunded and net amounts for Cash, Card, bKash and Nagad, both overall and by staff member. Cash totals include only actual cash portions and cash refunds; tendered change is excluded. A cash refund for a digital sale can make net cash negative. These are recorded gross payment movements, not bank settlement reconciliation; payment-provider fees and settlement timing are not included.

## Migration and checks

Apply `SplitPaymentMethods` and restart the backend. The migration preserves cash sales and backfills payment records for historical cash refunds. It does not change historical prices or stock quantities.

Production-mode checks cover split totals, single-method payments, missing references/confirmation, invalid amounts, non-cash change rejection, duplicate reference prevention, retry safety, exact refund shares, cash/original refund choices, cash reporting and held stock. Browser checks use a separate disposable database.
