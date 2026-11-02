# Phases — MICROC ContractFlow

Items are marked ✅ only when implemented **and** tested. See
`docs/FUNCTIONAL_DESIGN.md` §12 for the full plan.

## Phase 0 — Skeleton
- [x] Repo structure: `src/server` (API + tests), `src/client` (React + Vitest)
- [x] Dev mode: no login, role-switcher picks the active seeded user
- [x] i18n scaffold (English primary, Swedish selectable)
- [x] EF Core: LocalDB dev (`ContractFlow_Local`), SQLite tests
- [x] Neutral seed data (no real company names or contract values)

## Phase 1 — Internal planning & PAD
- [x] Customers / contracts / projects / work packages with versioned terms —
      billing is component-based per terms version (time billing: hourly |
      daily rate | none · PAD component · fixed price · mobilisation fee +
      expense markup), replacing the original binary contract model
- [x] Daily-rate component (full/half-day pricing, proportional split across
      projects; day-type rates for Saturday/Sunday/holiday with fallback chain;
      OT billed on top as rate/8 × OT% per hour over the threshold)
- [x] Customer & end-client master data (VAT, address, country, contact lists;
      end client optional — "same as customer" when identical)
- [x] Country-keyed holiday calendars — per-country providers incl. UK nation
      variants (GB-ENG/GB-SCT/GB-NIR), DE, FR, IT and US federal, with
      substitute/observance-day rules — plus `/api/meta/{calendars,countries,
      currencies}` powering searchable pickers (ISO 3166/4217 full lists)
- [x] Resource register + versioned compensation agreements
- [x] Planning calendar (month grid + week/day views with resource & project lanes)
- [x] Bookings + status flow (Forecast → … → Confirmed → … → Completed)
- [x] PAD ledger + monthly dashboard with drill-down
- [x] Carry-over, expiry, month-end closing (multi-month carry window,
      origin-tracked residuals re-carry or expire per ContractTerms.CarryOverMonths)
- [x] Additional PAD request/approval flow
- [x] Cancellation & rescheduling rules (stepped notice ladder — hours or fixed
      fee per rung; PAD contracts materialise charges as partial PAD utilisation,
      non-PAD contracts bill a cancellation line; Swedish-holiday business days)
- [x] Conflict warnings (double-booking, leave overlap)
- [x] Personal .ics calendar feeds (token-gated, rotatable)
- [x] My Work (resource assignment view)

## Phase 2 — Resource reporting
- [x] Assignment acknowledgement
- [x] Time reporting (waiting/standby, multi-project days)
- [x] Self-reporting without bookings — project access lists gate which
      resources may log unbooked work (GET /api/projects/mine); per-row
      billing mode (contract model vs. plain hours)
- [x] Travel records + mobilisation grouping (suggest/confirm/split)
- [x] Expenses + mileage + per diem
- [x] Documents: receipts on expenses + evidence on bookings (IFileStorage —
      local disk dev, blob later; ImageSharp thumbnails deferred)
- [x] Timesheet submission & approval (submit → approve/return/reject, locks rows)
- [x] Day calculation engine (TimeMath: day type, OT split, min-hours,
      proportional allocation, expense markup — actual/payable/billable kept separate)
- [ ] Authentication (Entra) — arrives here at latest

## Phase 3 — Money
- [x] Customer invoice specifications (generate from approved timesheets;
      draft → customer review → final → issued → paid; per-line explanations)
- [x] Subcontractor payment statements (payable hours/expenses/mileage/
      travel time/per diem at agreement rates; draft → final → paid)
- [x] Supplier invoice reconciliation (register incoming UE invoice, match
      against statement, per-kind diffs, approve → pay)
- [x] Revenue / cost / margin reporting (per project × month, internal roles)
- [x] Composable billing components on versioned terms (hourly / daily-rate —
      incl. Sat/Sun/holiday day rates + OT on top / PAD / one-off fixed price /
      cancellation charges)
- [x] Package-rate overrides (versioned WorkPackageRate per work package —
      set fields win over contract terms, empty fields inherit; covers
      hourly/day rates + mobilisation fee; mobilisation periods can be
      bound to a work package)
- [x] Resource × work-package rate matrix — project resource roster
      (ProjectResource, distinct from self-reporting access) feeds the
      selectors; adding a member snapshots the in-force resolved rate into
      NeedsReview cells on every work package; resolution merges per field
      resource cell > package row > contract; unbooked time rows can name
      their work package
- [x] Work-package-scoped compensation agreements — agreement scope cascades
      customer → contract → work package (payable side mirrors the billable
      matrix); resolution WP scope > contract scope > default
- [x] Competency tracking — ResourceCompetency list (label + optional expiry
      + optional reminder window) replacing free text; due/expired flags on
      the resource list
- [x] Work-package info fields — typed free-form fields (Address, Access,
      Contact…) replacing the single Address column
- [x] Timesheet → Invoiced leg — issuing a spec marks fully billed weeks
      Invoiced; specs carry revisions, so approved-but-unbilled rows (weeks
      crossing a month end) go on a supplement; `GET /api/invoices/unbilled`
- [x] One shared day-pricing pipeline (DayBilling) for approval, invoice,
      statement and cost — frozen billable == invoiced; one minimum and one OT
      threshold per contract-day across work packages
- [x] Billed history protected — terms/agreement/package-rate versions that
      priced a non-draft document can't be deleted or back-dated
- [ ] Self-reporting access at work-package level (currently project-level —
      review when the access model is revisited)
- [ ] Multi-currency, rate indexation (until then: rows in another currency
      are left out of a document's total and listed as warnings)
- [ ] Excel/PDF/CSV exports
- [ ] Separate invoice lines for OT and minimum-hours adjustment (today in
      the line explanation only)

## Phase 4 — Go live
- [ ] Azure deploy (SWA + App Service + SQL + Blob)
- Note: the microc.se domain (intended for the external "partner panel" /
      sign-in surface) is currently parked at hostek.se — DNS/hosting must be
      repointed when the panel goes live.
- [ ] microc.se "Logga in / Mina sidor" entry point
- [ ] Email notifications
- [ ] Backup strategy (optionally UNAS Pro via SMB)
- [ ] Retention/GDPR review

## Review & hardening (2026-10)
- [x] Month close: pre-close checklist + step-by-step preview, force with
      reason, strict ordering (previous closed, month ended), one transaction
- [x] Carry-over consumed before baseline (§4.3); late cancel ≥ 1 PAD;
      continuation overrun ①–④ on completion with actual hours
- [x] Additional PAD request can name the waiting booking — approval commits it
- [x] Reporting guards: no rows into submitted/approved weeks, none out of a
      closed month, ≤ 24 h/day, expense duplicate warning, one per-diem/day
- [x] Timesheet review history, required note on return/reject, admin reopen
- [x] Audit log — AuditEntry on every commercial save, `GET /api/audit`,
      Settings viewer (Admin/Finance)
- [x] Dev identity only in Development/Testing (401 elsewhere); no exception
      text in non-dev 500s
- [x] Role-aware navigation and dashboard; Swedish decimal comma everywhere

## Contract-document rules (2026-10) — all per-contract terms settings
- [x] One organisation register: party + optional end client (+ reference)
- [x] Carry-over basis (unused baseline default | whole baseline), round-up step
- [x] PAD invoicing: utilised only | lump sum (utilised + expired, carried PAD
      billed when it lands); Additional / auto-incurred PAD on separate lines
- [x] Shared booked day across projects of one contract (minimum split
      proportionally or per project; utilisation by actual hours)
- [x] Unbooked services consume PAD (15-min increments; shortfall needs
      Additional PAD approval before close)
- [x] Booking start time with notice rule (default 08:00, 4 business days)
- [x] Work packages outside the contract (billed by time, no PAD) and
      included-visit limit (travel billed beyond it)
- [x] Cancellation charges as share of the day rate on day-rate contracts
- [x] One-off scheduled charges replace the "fixed price" terms component
- [x] Baseline schedule grid vs contracted total; authorised representatives

## Backlog
- PM ↔ project assignment (§2.1 "assigned projects" — PMs currently see all)
- Standalone hourly activities consuming actual/8 PAD (§4.3)
- Roster snapshot cells freeze contract rates (later terms/indexation don't
  reach them) — decide whether cells should inherit by default
- Customer reviewer portal
- Forecasting/scenario UI
- Mitrandir read-API link (service identity, customer consent)
- Accounting-system export (Fortnox/Visma — TBD)
