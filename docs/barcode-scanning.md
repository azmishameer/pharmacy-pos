# Barcode scanning

Use a USB scanner in keyboard/HID mode configured to send Enter after each scan. Click **Scan product barcode** first. Typing a barcode and Enter follows the same path. No camera, driver integration or GS1 batch/expiry parsing is included.

## Register codes

An admin opens **Medicine barcodes**, searches for an approved medicine, selects the exact strength/manufacturer, and registers its printed barcode. Select Piece (one tablet, bottle, tube or other base stock unit), Strip or Box and enter the individual stock units in that pack. Multiple codes can identify one medicine. Codes are stored as text: leading zeros and letter case are preserved. Codes match exactly and must be 1–100 printable ASCII characters without spaces. Unknown or inactive codes produce an explicit error; they never create a medicine automatically.

Assignments are unique across medicines and packs. Retrying an unchanged registration is safe. Admins can retire a code; retired assignments and the creating/retiring user and timestamps remain in the database. Retired codes cannot be reused or silently reassigned. Operators can scan but cannot register or retire codes. Medicine catalogue approval is required before registration.

## Sales

Scan in **Sales counter**. The screen identifies the medicine and pack, then shows compatible sellable batches in expiry order. Confirm the physical batch and quantity, then choose **Add to cart**. The selling unit defaults to the scanned pack. Repeating a scan refreshes the selection; it does not silently add another item. Only approved, unexpired stock with verified MRP is offered. A barcode for a 30-piece box does not match stock recorded as 20-piece boxes. Clear the barcode filter to resume ordinary search.

The scanner identifies the product and pack, not a batch. Checkout still validates quantities, stock, prices and payment atomically. No stock is reserved or deducted by scanning.

## Receiving

Scan from the catalogue or **Stock & receiving** to open the medicine's receiving form. Enter and verify the actual quantities, pack conversions, batch, manufacturing/expiry dates and printed MRP. Scanning does not save a delivery, invent dates or bypass operator-entry approval.

## Validation

Automated production-mode integration checks cover authorization, CSRF, invalid mappings, exact/leading-zero matching, concurrent registration, pack compatibility, retirement and audit. Browser checks exercise scanner-style Enter input, pack preselection, cart entry and receiving against a disposable database. Physical scanner hardware is not available; compatibility assumes keyboard input with an Enter suffix.
