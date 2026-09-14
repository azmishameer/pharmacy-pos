# Medicine entry and catalogue review

Staff can enter medicines in the development preview. Admin submissions are approved
immediately; Operator and Manager submissions require individual admin review. Only
approved, active medicines appear in the ordinary catalogue. This does not create
saleable stock or set MRP, purchase cost, or discounts.

## Setup

Apply the `CatalogueBatchesAndReview` migration and restart the backend:

```sh
dotnet ef database update --project backend/PharmacyPos.Api -- --environment Development
```

The migration preserves existing medicines as approved and adds review metadata plus
a separate `medicine_batches` table. It has been tested against a disposable database;
applying it to the pharmacy database is the next user step. Reverting after adding
non-tablet records requires data planning because the old schema allows tablets only.

## Entry

- Manufacturer dropdown contains 26 common Bangladeshi companies, with **Other — enter
  name** for additional manufacturers. It is a curated starter list, not a licensing
  register. Source: [BAPI members directory](https://www.bapi-bd.com/members-directory.html),
  checked September 15, 2026. Names typed manually are matched case-insensitively;
  historical name changes and aliases are not automatically merged.
- Dosage forms include tablet, capsule, syrup, suspension, injection, cream, and others.
  Select the smallest stock unit for non-tablets (for example bottle, tube, or vial).
  Packaging conversions and prices remain a later stock-receiving step.
- Record each generic ingredient and its strength separately. Concentration units
  include mg/mL, mg/5mL, IU/mL, percentages, and per-dose units. Copy the label exactly;
  the application does not infer concentration, bottle size, or ingredient names.
- The optional first-batch section records batch number, manufacturing date if shown,
  and required expiry date. Manufacturing cannot follow expiry. Dates belong to the
  batch rather than the medicine. Month-only label date precision is not supported yet;
  do not invent a day. Existing-medicine batch receiving is still a later step.
- **Keep the form open to add another medicine** is enabled initially. Confirmed saves
  clear brand, ingredients, and batch details and retain manufacturer/form preferences.
  Failed or uncertain saves preserve the form for a safe retry.

## Review and permissions

Admins see pending medicine submissions, ingredients, manufacturer, form, stock unit,
classification and batch dates below the catalogue. Each can be approved or rejected;
rejection requires a reason. Staff see their own pending/rejected submissions only.
Lists show up to 100 entries; approved entries leave the queue. Rejected records stay
in the database and a corrected replacement can be entered by staff or an admin.
Editing/resubmitting the original record and linking replacement records are not yet
implemented. Creator and reviewer identities, timestamps and review notes are stored.

Approval endpoints require Admin authorization and CSRF protection. A conditional
update prevents concurrent reviewers from overwriting one another. Medicine creation
uses staff authorization, CSRF, transactional batch/reference creation, duplicate
checks and request IDs for idempotent retries. Non-rejected brand/manufacturer/form/
ingredient-strength duplicates are rejected; equivalent unit expressions are not
canonicalized. Catalogue endpoints remain development-only.

Stock receipt approval, CSV exports, verified batch MRP, purchase costs, offers, and
sales are separate planned workflows. Catalogue approval never posts stock quantities.
Operator account creation UI is not implemented yet; role permissions are already
supported and exercised using isolated test accounts.

## Verification

Backend integration checks cover authentication, roles, CSRF, catalogue creation,
replay and concurrent duplicates, operator submission, hidden pending entries, liquid
strengths, batch date validation, admin review, rejection/reentry, and audit metadata.
Frontend build and lint also pass. No sample medicines were inserted into the user's
database during testing.

Browser verification also passed against the disposable database: Operator sign-in,
manufacturer Other/manual entry, syrup/bottle selection, concentration, batch dates,
save-and-add-another reset, Admin review of the saved dates, and approval into the
catalogue. Date fields handle native input events, and missing/reversed dates are
checked before submission as well as on the server.
