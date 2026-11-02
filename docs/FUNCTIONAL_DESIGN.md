# MICROC ContractFlow — Functional Design

**Contract, Resource, Time & Financial Management System**

Status: Draft for review (per requirements spec §32 — functional design precedes
implementation).

Sources: "Tidrapportering ChatGPT.docx" (main functional spec, hourly-contract
spec, minimum-hours annex) and "Tidrapportering Devin.docx" (project idea), both
kept in OneDrive under *Planeringsprogram/Underlag* — **not** committed to this
repository since they reference real commercial partners.

---

## 1. Purpose and scope

A web application covering the full chain from planning to money:

- **Plan** — a central calendar where administrators book inspectors/
  subcontractors onto project assignments.
- **Execute** — each resource sees personal assignments, acknowledges bookings,
  reports working time (incl. waiting/standby), travel, mileage, expenses with
  receipts and per diem allowances, and submits timesheets.
- **Compensate** — approved records produce a payment statement per resource,
  matched against the subcontractor's own supplier invoice.
- **Bill** — the same approved records independently produce customer billing
  under each contract's commercial model (PAD-based or hourly-based).
- **Follow up** — monthly closing, reconciliation, forecasting and audit trail.

The system serves MICROC internally first, then a limited set of
subcontractors, then optionally customer reviewers. It will later be reachable
from a public microc.se page ("Logga in / Mina sidor").

Explicitly out of scope (initially): formal accounting invoices, bank payments,
payroll — the system produces **reliable supporting documentation and exports**
for the accounting system.

### 1.1 The five separations (fundamental design principle)

Every quantity is kept in one of five separate ledgers even when they refer to
the same assignment:

| Layer | Question answered |
|---|---|
| Planning | What is expected to happen |
| Execution | What work and travel actually occurred |
| Compensation | What MICROC owes the resource |
| Customer billing | What MICROC may invoice the customer |
| Financial performance | Revenue, cost, margin, forecast |

Concretely: three hour quantities — **actual, payable, billable** — are stored
and reported separately per assignment. Customer PAD utilisation never
determines subcontractor pay; inspector rates never determine customer rates.
A booking, a work shift and a journey are three different objects: one booking
can produce several time reports; one journey can span several Booked Days.

---

## 2. Application structure and user roles

### 2.1 Roles

| Role | Access |
|---|---|
| **Administrator** | Everything: contracts, rates, resources, planning, approvals, PAD, billing, payments, users, audit |
| **Project Manager** | Assigned projects: plan, book, monitor PAD, review/approve time and expenses within authority (permissions configurable) |
| **Resource / Subcontractor** | Own assignments, calendar feed, acknowledgement, time/travel/expense reporting, own timesheets and payment statements. Never other resources' rates or MICROC margins |
| **Finance** | Approved financial records, invoice register, supplier invoice matching, exports, VAT |
| **Customer reviewer** (optional, per project) | Customer-facing timesheets, Additional PAD approval, selected reports. Never internal costs |
| **Dev mode (local only)** | No login initially; a dev role-switcher banner selects which seeded user the API treats the request as. Removed/disabled before deployment |

### 2.2 Main sections (navigation)

1. **Dashboard** — role-aware landing (management vs resource view).
2. **Plan** — the calendar: month / week / day / resource lanes / project lanes /
   multi-project overview. Bookings created, moved (as reschedule), cancelled
   here. Conflict warnings inline.
3. **Projects & Contracts** — customer/contract/project/location hierarchy,
   versioned commercial terms, PAD baseline import, estimated quantities,
   documents.
4. **Resources** — the register: competencies, classifications, individual
   compensation agreements, availability/leave, .ics feed links.
5. **My Work** (resource view) — assignments awaiting acknowledgement, enter
   time/travel/expenses/mileage/per diem, submit timesheet, track status.
6. **Approvals** — PM/admin queue: timesheets, expenses, Additional PAD
   requests; approve / return / reject with comments.
7. **Month-End** — per project × month: the PAD dashboard with drill-down,
   closing checklist, preview, close; carry-over/expiry calculation display.
8. **Economy** — customer invoice specifications + register, subcontractor
   payment statements, supplier invoice reconciliation, project
   revenue/cost/margin.
9. **Reports** — project, resource and commercial reports; Excel/PDF/CSV export.
10. **Settings** — users & roles, contract rule sets, holiday calendars,
    notification preferences, audit log, document store.

---

## 3. Conceptual domain model

Grouped, not yet a database schema — but close.

**Commercial structure**
- `Customer` = one organisation register for both roles. A contract names its
  **party** (who signs and pays MICROC) and optionally an **end client** from
  the same register — the organisation the work is ultimately for when an
  intermediary (e.g. a larger consultancy) holds the contract. No end client =
  direct contract. One organisation can be the party on one contract and the
  end client on another; reports group by the *effective* end client (end
  client, else the party), and invoice specifications carry the end client
  and its reference, frozen at generation.
- `Contract` → versioned `ContractTerms` (rates, PAD rules, cancellation rules,
  travel/expense policy, currency, indexation — effective-dated, never
  rewritten)
- `Project`, `Location`
- `EstimatedQuantity` (tender quantities for monitoring, not guarantees)

**Resources**
- `Resource` (person: employee / subcontractor / directly-contracted-by-customer),
  competencies, availability/leave, active flag
- `CompensationAgreement` (versioned, per resource per contract scope: hourly
  rates by day type, OT rules, travel-time %, mileage rate, per diem policy,
  expense policy, currency, effective dates)

**Planning**
- `Assignment`/`Booking` — project, activity ref, date(s), location, work
  description, resource, planned start/duration, expected PAD, status, booking
  source/evidence, customer contact
- Booking statuses: Forecast → Tentative → Requested → **Confirmed (Booked Day)**
  → Acknowledged → In progress → Completed / Cancelled / Rescheduled
- A *Confirmed Booked Day* is what reserves PAD; a forecast/tentative activity
  never does. Resource acknowledgement is a separate flag — a missing
  acknowledgement never silently releases the booking.

**Execution**
- `TimeEntry` — date, attendance window, active work, waiting/standby periods,
  breaks, release time, activity category, project split, description. Hour
  quantities: actual / payable / billable (+ calculated minimum-hours
  adjustment shown separately)
- `TravelRecord` — legs with mode, times, distance; links to a `MobilisationPeriod`
- `MobilisationPeriod` — a continuous attendance period (grouping suggested,
  admin confirms/splits/merges) that may carry one customer mobilisation fee
- `Expense` + receipt files, `MileageEntry`, `AllowanceClaim` (per diem)
- `Timesheet` — submission container per resource per period; status flow
  Draft → Submitted → Reviewed → Approved → Invoiced (Rejected/Returned loops
  back). Approved is locked; corrections via auditable adjustments.

**PAD accounting**
- `PadAccount` = derived view per `Project` × calendar month, computed from an
  append-only `PadTransaction` ledger (see §4)
- `AdditionalPadRequest` — draft/submitted/awaiting customer/approved/rejected/
  withdrawn/utilised/invoiced, with reason, quantity, rate, reference

**Billing & payment**
- `InvoiceSpecification` (draft → customer review → final → issued → paid;
  corrections via revisions/credit notes)
- `SubcontractorStatement` (approved payable basis per resource per period)
- `SupplierInvoice` (registered incoming invoice, matched against statement)

**Cross-cutting**
- `Document` (attached to contracts, bookings, expenses, approvals…, versioned)
- `AuditEntry` — every commercially relevant change: who, when, old, new, reason
- `Notification` — configurable per user
- `CalendarFeed` — per-resource tokenised .ics URL

---

## 4. The PAD model

PAD accounting is an **append-only ledger** per project-month. Every quantity
shown to the user is a sum of transactions, so every number on the monthly
dashboard drills down to the bookings, time entries and approvals behind it.

### 4.1 Transaction types

| Transaction | Effect |
|---|---|
| `BaselineSet` | Monthly contractual allocation (entered or imported; amendable with audit) |
| `CarryOverIn` | Arrives from previous month at close |
| `Commit` | Reserves PAD when a Booked Day is confirmed |
| `Release` | Frees a commitment (timely cancellation, re-plan) |
| `Utilise` | Consumes PAD from completed work / chargeable cancellation |
| `AdditionalApproved` | Customer-approved PAD beyond allocation |
| `AutoAdditional` | Automatically incurred, chargeable PAD (continuation overflow) |
| `BorrowNextMonth` | Continuation rule consuming next month's baseline |
| `CarryOverOut` | Leaves to next month at close |
| `Expire` | PAD that dies (unused carry-over, non-transferable baseline) |
| `Adjustment` | Documented correction by an authorised user |

### 4.2 Derived quantities (the monthly dashboard)

```
Total available  = Baseline + CarryOverIn + AdditionalApproved
Free             = Total available − Utilised − Committed − Expired − CarryOverOut
                   (BorrowNextMonth reduces *next* month's free)
```

Worked example from the spec: Baseline 8.00 + carry-over 1.00 = 9.00 available;
utilised 2.50, committed 4.00 → free 2.50. All eight categories (baseline,
carry-over, committed, utilised, free, additional, expired, next-month-borrowed)
are shown individually and reconcilable.

### 4.3 Contract rules (configurable per contract; first config = the initial PAD contract)

- 1 PAD = 8 h; a confirmed Booked Day commits **min 1.00 PAD** per inspector;
  longer days proportionally more; standalone hourly activities consume
  actual/8 with no minimum.
- **Carry-over**: only unused *baseline* generates it; max 20 % of the
  *unused* baseline rounded up to whole PAD, capped at actual unused
  (baseline 12, 3 used → 9 unused → 1.8 → 2 carried; 11.5 used → 0.5 unused →
  0.5 carried). Basis (unused or whole baseline), percentage and rounding step
  are contract settings; moves only to the
  immediately following month; **carry-over is consumed before baseline**;
  cannot be carried again; expires at end of receiving month; project-specific;
  may enter a month with zero baseline.
- **Cancellation**: deadline 4 business days (business-day calendar per
  contract). Timely → release, no PAD. Late → normally ≥1.00 PAD consumed;
  commenced work counts per working/standby rules.
- **Reschedule** = cancel + new booking (the original keeps its own PAD
  consequences; never a silent move).
- **Partial availability**: a new Booked Day may combine e.g. 0.50 free PAD +
  0.50 approved Additional PAD.
- **Continuation beyond commitment** (an already-confirmed Booked Day overruns)
  allocates extra utilisation in order: ① remaining uncommitted current-month
  PAD → ② approved Additional PAD → ③ next-month baseline (reduces that month's
  free, flags conflicts) → ④ auto-incurred Additional PAD. The source split is
  always displayed.
- A new booking needing Additional PAD can be *prepared* but is not
  *commercially confirmed* until approved.
- Forecast/tentative items never touch the ledger.

---

## 5. The calculation engine

A pure, deterministic pipeline — the most test-critical component. Every
contract rule from the spec maps to a unit test built on the spec's own worked
examples.

Per working day (per resource):

1. Sum activity hours per project/category.
2. Resolve **day type** (weekday / Saturday / Sunday / public holiday) via the
   contract's holiday calendar (Swedish default, configurable per contract).
3. Split ordinary vs overtime (e.g. OT from 9th hour, weekend multipliers —
   configurable, no compounding unless the contract says so).
4. Apply the **minimum** model (none / per booked day / full-day bookings only /
   per attendance day / per assignment / custom with explicit precedence) —
   produces the minimum-hours *adjustment* line, never overwriting actual hours
   and never itself triggering OT.
5. Allocate across projects (proportional to activity hours, fixed shares, or
   all-to-one).
6. Round per contract (per day / per line / per invoice — open question, §11).
7. Compute amounts with the applicable versioned rate set.

Run **twice with different rule sets**:

- `CustomerBillingCalculator` → billable hours, PAD, mobilisation fees,
  recharge expenses → invoice specification lines.
- `CompensationCalculator` → payable hours, internal rates, travel-time %,
  mileage, per diem, reimbursables → payment statement lines.

Every output line carries a human-readable explanation ("Saturday: 8 h × 150 %
+ 2 h OT × 175 %; minimum-hours adjustment +5.0 h") — the spec requires each
invoice row to explain how it was computed, and months to be re-reconcilable
years later.

Worked examples that become tests (from the spec):
- 6 h across projects A+B with 8 h minimum → A 2.67 h / B 5.33 h.
- 10 baseline days, 7 used → 3 unused → carry-over 1 (20 % of 3 = 0.6, rounded
  up), 2 expire. (With the whole-baseline basis: 20 % of 10 → 2 carried, 1 expires.)
- Saturday 10 h, ordinary 8 → 8 h × Sat rate + 2 h × Sat rate × 1.75.
- Expense 1 200 + 10 % mark-up → 1 320.
- Mobilisation fixed fee regardless of actual cost.
- Min-hours: 3 actual → +5.0 adjustment → 8 payable / 8 billable, shown as
  separate lines; multiple activities same day share one minimum.

---

## 6. Workflows

### 6.1 Planning → billing (the main flow)

```
Admin plans activity (Forecast/Tentative — no PAD)
      → customer instruction arrives → Requested (+evidence attached)
      → Confirm: conflict check + PAD check → Committed = Booked Day
           · insufficient PAD → prepare AdditionalPadRequest, booking waits
      → Resource notified → Acknowledges (or flags conflict — PAD stays committed)
      → Execution: resource reports day(s) against the booking
           · time (attendance/active/waiting/breaks), travel legs, expenses
      → Timesheet submitted weekly/monthly
      → MICROC approval (approve / return / reject / adjust-with-reason)
      → Utilise: PAD consumed per contract rules; payable & billable computed
      → Month-end close → customer invoice spec + subcontractor statement
```

### 6.2 Travel / expenses / compensation flow

```
Booking confirmed → resource may create TravelRecords (or admin pre-plans)
   MobilisationPeriod suggested from consecutive attendance days; admin
   confirms/splits/merges — one customer mobilisation fee per period (contract)
Resource registers: mileage (own flow), expenses (+receipts, incl. currency),
   per diem claim (per their policy — employee vs subcontractor rules differ)
Approval: Draft → Submitted → Under review → Approved / Returned / Rejected
   → included in SubcontractorStatement
Duplicate detection warns; an expense can never be reimbursed or recharged twice.
Three flags per expense line: reimbursable-to-resource / rechargeable-to-customer /
   included-in-rate — reimbursable and rechargeable are independent; included-in-rate
   excludes both (the cost is already covered by the rate). A rechargeable line
   needs a project.
Later: supplier invoice registered → matched against statement → diffs shown.
```

### 6.3 Cancellation & rescheduling

Deadline timer per contract (default 4 business days before booked start).
Timely → `Release`. Late → `Utilise` ≥1 PAD. Reschedule = new booking +
cancel path on the old one; original booking and evidence preserved.

### 6.4 Month-end closing

```
Pre-close checklist (blocking warnings):
   unsubmitted timesheets · unapproved time · unresolved bookings
   pending cancellations · unresolved Additional PAD · unapproved expenses
   travel needing review · calculation inconsistencies
Preview: final utilisation, carry-over + expiry calculation shown step-by-step,
   Additional PAD, billing basis, compensation basis
Admin approves → month locked → carry-over transactions written to next month
Corrections after close: controlled reopen or adjusting entries — all audited.
```

---

## 7. Calendar and personal feed

### 7.1 Planning calendar UI

- Views: month / week / day; **resource lanes** (rows = people) and
  **project lanes**; multi-project overview.
- Colour = booking status (forecast hatched → confirmed solid → acknowledged →
  completed; cancelled struck through).
- Conflict warnings: double-booked resource, overlapping assignments,
  unavailable/on leave, impractical consecutive travel, insufficient PAD,
  approval deadline approaching.
- Drag to re-date = creates reschedule (new booking + old cancelled), never a
  silent move. Copy for forecast scenarios.
- Booking panel: all §5.1 fields + evidence attachments + original-vs-current
  diff.

### 7.2 Personal .ics feed (Outlook etc.)

- `GET /api/calendar/{feedToken}.ics` — tokenised, per resource, regenerable
  (revokes the old URL). No login needed for the feed itself; the token is the
  secret — standard practice for calendar subscriptions.
- Emits VEVENTs for the resource's confirmed/acknowledged bookings and
  mobilisation travel days (title, location, times, description).
- Subscribed via Outlook "Add calendar → Subscribe from web"; read-only,
  refreshes automatically.
- Later option: Microsoft Graph two-way sync — not needed for v1.
- An admin/PM-level feed per project is a cheap follow-up.

---

## 8. Dashboards and reports

- **Monthly PAD dashboard** (§19 of spec): per project×month all PAD
  categories with drill-down to source records.
- **Multi-month overview**: original vs amended baseline, commitments,
  utilisation, forecast, additional, carry-over, expiry across the contract
  period.
- **Resource dashboard**: upcoming assignments, availability, leave, overlaps,
  workload (actual hours — deliberately independent of PAD), pending
  timesheets/expenses, travel commitments.
- **Management dashboard**: unsubmitted timesheets, pending approvals,
  unprocessed expenses, upcoming mobilisations, invoice status, quantity
  utilisation vs estimates, revenue/cost/margin.
- **Forecasting**: tentative entries → expected PAD demand, shortfalls,
  additional need, carry-over/expiry, resource conflicts — never touching real
  balances.
- **Reports**: monthly PAD/utilisation, baseline-vs-actual, booked days,
  cancellations, resource timesheets/hours/travel/mileage/expenses/allowances,
  billing basis, payment statements, project income/direct cost/margin,
  estimated-vs-actual quantities, outstanding invoices, forecast to
  completion. Export: **Excel and PDF; CSV where useful**.

---

## 9. Technical architecture

Same building blocks as Mitrandir, for reuse and familiarity:

| Layer | Choice |
|---|---|
| Frontend | React 19 + TypeScript + Vite, Tailwind 4, react-router, zustand, dnd-kit (calendar drag), lucide icons, Vitest + Testing Library |
| i18n | `react-i18next` — English primary, Swedish selectable, all UI strings in resource files from day one |
| Backend | ASP.NET Core, EF Core, Microsoft.Identity.Web (later), ImageSharp (receipt thumbnails/validation), Azure.Storage.Blobs, Azure.Communication.Email (later) |
| DB (dev) | SQL Server LocalDB — matches future Azure SQL; tests on SQLite in-memory (Mitrandir pattern) |
| Tests | xunit + WebApplicationFactory backend integration tests; Vitest pure-logic tests; **every commercial rule = unit tests built on spec examples** |
| Storage | `IFileStorage` abstraction → local disk dev, Azure Blob prod |
| Excel/PDF | ClosedXML (xlsx) + QuestPDF or generated print-HTML → PDF |

Repo layout mirrors Mitrandir: `src/server/ContractFlow.Api` +
`ContractFlow.Api.Tests`, `src/client/`, `docs/`, `AGENTS.md`, `PHASES.md`.

### 9.1 Auth strategy (later, but designed for now)

- All API endpoints carry role requirements from day one (`[Access(...)]`-style
  guard pattern as in Mitrandir). In dev, a middleware maps a header/dev-user
  picker to a seeded user; in production the same claims come from Entra ID.
- MICROC staff: MICROC Entra (M365). Subcontractors: open question — Entra B2B
  guests vs app-local accounts (see §11).
- Never trust a client-sent user id — server sets it (Mitrandir gotcha applies).

### 9.2 Deployment path

Local now → later: Azure Static Web App (client) + App Service (API) + Azure
SQL + Blob Storage, custom domain under microc.se (e.g. `app.microc.se`) with a
"Logga in / Mina sidor" link from the public site — same shape as Mitrandir.

UNAS Pro assessed and **not recommended** for hosting: no official Docker/app
support (SSH-hackable Debian base but unsupported and fragile across UniFi OS
updates, ARM-only images), and the service must be internet-facing anyway for
external subcontractors — which would put TLS, firewalling and uptime on the
office NAS. Legitimate UNAS role: optional off-site backup target for the DB
and receipt files via SMB.

### 9.3 Data principles

- Append-only PAD ledger; versioned contract terms & compensation agreements
  (effective dates — historical records reproduce the rules of their time).
- Audit log on all commercially relevant changes.
- Original amounts + currency always preserved alongside converted values.
- Retention: Swedish bookkeeping act — 7 years for accounting-relevant data;
  personal-data minimisation reviewed before go-live.

---

## 10. Security & privacy notes

- Subcontractors see only their own data; never other resources' rates, never
  customer pricing or margins.
- Customer reviewer role is per-project opt-in and excludes internal economics.
- .ics tokens are unguessable (128-bit), per-resource, regenerable; feed
  contains no rates or commercial terms — only booking title/time/location.
- Secrets in user-secrets/env only; nothing sensitive committed.
- Real customer/contract names are entered as data by the admin — never
  hardcoded in code, tests or seeds (neutral examples like "Example Org").

---

## 11. Open questions needing decisions before/while building

From the Devin doc's list plus what the spec leaves open:

1. **Which contract model first?** The spec names two PAD-model projects as
   initial projects but also details an hourly reference contract. Both engines
   are designed anyway — confirm PAD-first matches reality. *Also: actual
   contract values (rates, baselines) are entered as configuration — nothing
   real goes into seeds or tests.*
2. **Rounding**: per day, per invoice line, or per invoice — per contract?
3. ~~**Unused non-transferable PAD**: expire silently or billed anyway?~~
   Decided per contract (`PadInvoicing`): lump-sum PAD contracts bill expired
   PAD in the month it expires (consumed allocation = opening carry + baseline
   − carry-out); other contracts bill utilised PAD only.
4. ~~**Currencies**: how many, and which date governs the conversion rate?~~
   Decided: any ISO currency per row (expenses, per diem). At month end the
   rows are summed per currency and converted to the document currency
   (contract terms for the invoice specification, compensation agreement for
   the statement) at the rate of the document's month, from a per-month rate
   table entered by finance. Original amount, currency and rate stay on every
   converted line; a missing rate keeps the rows out and blocks finalizing; a
   rate that priced a document beyond draft is locked. Automated import from a
   central bank is a later option.
5. **Offline reporting** on sites without coverage — needed or out of scope?
6. **Per diem**: follow Skatteverket payroll rules, or internal policy per
   subcontractor agreement?
7. **Approval deputisation**: who approves when the PM is away?
8. **Accounting system**: Fortnox, Visma, other? Which export format?
9. **Subcontractor login**: Entra B2B guests, or app-local accounts with MFA?
   (Decides the auth work in Phase 2.)
10. **Holiday calendar**: Swedish for all, or the customer/site country's?
11. **Notifications**: in-app only initially, or email from day one?
12. **microc.se integration shape**: subdomain (`app.microc.se`) vs path —
    affects SWA custom-domain setup later.

---

## 12. Staged implementation plan

Spec phases preserved; re-sequenced for "local first, auth later":

| Phase | Content |
|---|---|
| **0 — Skeleton** | Repo, solution, CI-shape scripts, dev role-switcher, i18n scaffold, EF Core + LocalDB/SQLite-test setup, seed data with neutral examples |
| **1 — Internal planning & PAD** | Customers/contracts/projects/locations with versioned terms; resource register + compensation agreements; planning calendar; bookings + statuses; PAD ledger + monthly dashboard; carry-over/expiry/month-end close; Additional PAD flow; cancellation/rescheduling; conflict warnings; .ics feeds |
| **2 — Resource reporting** | Assignment acknowledgement; time reporting (incl. waiting/standby, project splits); travel + mobilisation grouping; expenses + receipts (ImageSharp); mileage; per diem; timesheets; approval queues — **auth arrives here at latest** |
| **3 — Money** | Customer invoice specifications + register + draft-review flow; subcontractor payment statements; supplier invoice reconciliation; revenue/cost/margin; multi-currency; indexation; Excel/PDF exports |
| **4 — Go live** | Entra auth, Azure deploy, microc.se "Mina sidor" link, email notifications, backups (optionally to UNAS via SMB), retention review |
| **Later** | Customer reviewer portal, forecasting/scenario UI polish, Mitrandir read-API link (separate service identity, customer consent), accounting-system export integration |

Each phase lands tested: backend integration tests (allowed + forbidden paths),
pure-logic tests for the calculation engine, `PHASES.md` updated.
