# Low-stock and near-expiry alerts

Apply `StockAlertThresholds` and restart the backend. **Stock alerts** is available
from both the medicine catalogue and the sales counter to admins and operators.
Opening alerts within the counter preserves its cart when returning. Lists refresh
once a minute while open; the Refresh button checks immediately.

## Low stock

Admins use **Set minimum stock**, find an approved active medicine, enable monitoring,
and enter a non-negative whole-number minimum in its base units (individual tablets,
capsules, bottles, etc.). A reason is required. The alert appears at or below the minimum.
Zero means out-of-sellable-stock only. Unconfigured or disabled thresholds are null and
are not monitored; no arbitrary default is assigned. The empty-list message explains this.

The quantity aggregates all sellable lots for that medicine using the sales counter's
eligibility rules. It includes approved restocked returns and subtracts completed sales.
Pending receiving entries, held customer returns, expired or disposed stock, unverified
MRP and inactive/unapproved medicines do not inflate sellable stock. Stock with missing
purchase cost can still count, consistent with the checkout rules.

Only admins can read the threshold-management list, change settings or view history.
Both roles can read current alert quantities and configured minimums. Writes require
CSRF protection, reject stale versions, and deduplicate unchanged retries. Changes,
previous values, admin names, reasons and times remain in audit history (latest 50 shown).
Disabling monitoring does not delete audit history or stock.

## Near expiry

Approved receiving lots with a positive remaining physical balance and expiry between
Bangladesh today and today + 90 days, inclusive, appear earliest first. A lot expiring
today is labelled **Expires today**, consistent with existing end-of-day sale eligibility.
Sold-out, disposed and already-expired lots are excluded. Expired stock stays in the
existing unsellable inventory workflow.

Approved stock awaiting MRP verification or belonging to an inactive/unapproved medicine
still appears as held, since it physically needs attention. Pending deliveries and held
customer returns remain in their separate review inventories. These are on-screen alerts;
no email, push notifications or automatic orders are sent. Nothing is automatically
approved, disposed or repriced by an alert.

The lists have search, stable pagination and explicit public fields; purchase cost and
profit information are never returned to operators. Integration checks cover permissions,
equality/zero/unset thresholds, expiry boundaries, sales, returns and audit history.
