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
- [x] Customers / contracts / projects / locations with versioned terms
- [x] Resource register + versioned compensation agreements
- [ ] Planning calendar (month/week/day, resource & project lanes)
- [ ] Bookings + status flow (Forecast → … → Confirmed → … → Completed)
- [ ] PAD ledger + monthly dashboard with drill-down
- [ ] Carry-over, expiry, month-end closing
- [ ] Additional PAD request/approval flow
- [ ] Cancellation & rescheduling rules
- [ ] Conflict warnings
- [ ] Personal .ics calendar feeds

## Phase 2 — Resource reporting
- [ ] Assignment acknowledgement
- [ ] Time reporting (waiting/standby, multi-project days)
- [ ] Travel records + mobilisation grouping
- [ ] Expenses + receipt upload (ImageSharp)
- [ ] Mileage, per diem
- [ ] Timesheet submission & approval
- [ ] Authentication (Entra) — arrives here at latest

## Phase 3 — Money
- [ ] Customer invoice specifications + register + draft review
- [ ] Subcontractor payment statements
- [ ] Supplier invoice reconciliation
- [ ] Revenue / cost / margin reporting
- [ ] Multi-currency, rate indexation
- [ ] Excel/PDF/CSV exports

## Phase 4 — Go live
- [ ] Azure deploy (SWA + App Service + SQL + Blob)
- [ ] microc.se "Logga in / Mina sidor" entry point
- [ ] Email notifications
- [ ] Backup strategy (optionally UNAS Pro via SMB)
- [ ] Retention/GDPR review

## Backlog
- Customer reviewer portal
- Forecasting/scenario UI
- Mitrandir read-API link (service identity, customer consent)
- Accounting-system export (Fortnox/Visma — TBD)
