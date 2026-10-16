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
- [x] Customers / contracts / projects / locations with versioned terms —
      billing is component-based per terms version (time billing: hourly |
      daily rate | none · PAD component · fixed price · mobilisation fee +
      expense markup), replacing the original binary contract model
- [x] Daily-rate component (full/half-day pricing, proportional split across
      projects; day-type rates for Saturday/Sunday/holiday with fallback chain;
      OT billed on top as rate/8 × OT% per hour over the threshold)
- [x] Customer & end-client master data (VAT, address, country, contact lists;
      end client optional — "same as customer" when identical)
- [x] Country-keyed holiday calendars (SE/NO/DK/FI/none) with `/api/meta/calendars`
      dropdown — business-day math and holiday day-type per contract country
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
- [ ] Multi-currency, rate indexation
- [ ] Excel/PDF/CSV exports
- [ ] Timesheet → Invoiced leg once an invoice covers the period

## Phase 4 — Go live
- [ ] Azure deploy (SWA + App Service + SQL + Blob)
- [ ] microc.se "Logga in / Mina sidor" entry point
- [ ] Email notifications
- [ ] Backup strategy (optionally UNAS Pro via SMB)
- [ ] Retention/GDPR review

## Backlog
- Audit log (admin-visible change history, like Mitrandir)
- Customer reviewer portal
- Forecasting/scenario UI
- Mitrandir read-API link (service identity, customer consent)
- Accounting-system export (Fortnox/Visma — TBD)
