# Receipts and return policy

Admins open **Receipt settings** from the medicine catalogue to enter the pharmacy/hospital name, address and optional PNG logo. Logos must be at most 100 KB and 1024 × 1024 pixels. A header preview is available before saving. Only admins can change these settings; changes retain the administrator and timestamp in immutable database revisions.

New sales retain the branding revision used at checkout. Reprinting a past sale never replaces its branding with newer details. Sales made before this feature retain the generic Pharmacy POS heading because their original branding was not recorded.

Receipts show the centred logo, name and address, followed by receipt number, Bangladesh purchase time, operator, sold items, discounts, named charges, totals, rounding, cash and change. The fixed footer requires the original receipt and states the 15-day return/refund limit. The exact return deadline is printed in Bangladesh time.

## Printing

Use **Print receipt** on a completed sale or saved receipt. In the print dialog select your 80 mm printer/paper, 100% scale, and disable browser headers and footers. The content is 72 mm wide with 4 mm page margins, with wrapping for long names and addresses. Paper length is controlled by the printer driver; the application does not request a fixed roll length. Save as PDF can be used before obtaining a printer. Actual paper feed, cutter and hardware margins must be checked on the eventual printer.

## Returns

Find the original POS receipt number. Return eligibility ends exactly 15 elapsed days after the saved purchase timestamp (the exact deadline is inclusive). Both admin and operator requests are checked on the server; there is no admin bypass. An expired receipt can still be looked up, but its items cannot be selected for refund. Do not hand over cash on an expired receipt.

The cashier must confirm that the customer presented the original receipt, in addition to receiving the complete selected items and issuing the cash refund. The confirmation is stored with the cashier audit. Historical returns have an unknown confirmation, rather than inventing evidence for old transactions.

Previously committed refund requests can still be retried after the deadline to recover their result without duplicating cash/stock records. New returns remain limited to complete receipt lines. Returned stock remains held until an admin approves resale.
