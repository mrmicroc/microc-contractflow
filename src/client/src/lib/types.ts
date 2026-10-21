// Mirrors the API DTOs in ContractFlow.Api/Models/Dto (camelCase JSON).

/** Billing-component keys on the terms version in force: 'hourly' |
 * 'dailyRate' | 'pad' | 'scheduledCharges' (the contract has one-off dated
 * amounts). A contract may carry several at once. */
export type ContractComponent = 'hourly' | 'dailyRate' | 'pad' | 'scheduledCharges';
export type CarryOverBasis = 'unusedBaseline' | 'baseline';
export type PadInvoicing = 'utilised' | 'allocationConsumed';
export type SharedDayMinimum = 'proportional' | 'perProject';
export type WorkPackagePricing = 'contract' | 'outsideContract';
export type TimeBilling = 'none' | 'hourly' | 'dailyRate';
export type ResourceType = 'employee' | 'subcontractor' | 'directlyContracted';
export type LeaveType = 'vacation' | 'leave' | 'unavailable';
export type PerDiemPolicy = 'none' | 'statutory' | 'custom';

export interface ContactPerson {
  name: string;
  role: string | null;
  email: string | null;
  phone: string | null;
}

export interface CustomerSummary {
  id: string;
  name: string;
  externalReference: string | null;
  vatNumber: string | null;
  country: string | null;
  isActive: boolean;
  /** Contracts where the organisation is the contract party. */
  contractCount: number;
  /** Contracts an intermediary holds where the organisation is the end client. */
  endClientContractCount: number;
}

/** A contract where the organisation is the end client, named with the
 * intermediary (contract party) that holds it. */
export interface EndClientContract {
  id: string;
  name: string;
  customerId: string;
  customerName: string;
  endClientReference: string | null;
  startDate: string;
  endDate: string | null;
  isActive: boolean;
}

export interface CustomerDetail {
  id: string;
  name: string;
  externalReference: string | null;
  vatNumber: string | null;
  addressLine: string | null;
  postalCode: string | null;
  city: string | null;
  country: string | null;
  contacts: ContactPerson[];
  notes: string | null;
  isActive: boolean;
  contracts: ContractSummary[];
  endClientContracts: EndClientContract[];
}

export interface CustomerRequestBody {
  name: string;
  externalReference: string | null;
  vatNumber: string | null;
  addressLine: string | null;
  postalCode: string | null;
  city: string | null;
  country: string | null;
  contacts: ContactPerson[];
  notes: string | null;
  isActive: boolean;
}

/** GET /api/meta/{calendars,countries,currencies} row — code + English name. */
export interface MetaEntry {
  code: string;
  name: string;
}

/** GET /api/contracts/{id}/work-packages row — scope-picker option. */
export interface ContractWorkPackage {
  id: string;
  name: string;
  projectId: string;
  projectName: string;
}

export interface ContractSummary {
  id: string;
  customerId: string;
  name: string;
  externalReference: string | null;
  components: ContractComponent[];
  startDate: string;
  endDate: string | null;
  isActive: boolean;
}

export interface ContractDetail {
  id: string;
  customerId: string;
  customerName: string;
  /** End client from the same register; null = direct contract. */
  endClientId: string | null;
  endClientName: string | null;
  endClientReference: string | null;
  name: string;
  externalReference: string | null;
  components: ContractComponent[];
  startDate: string;
  endDate: string | null;
  isActive: boolean;
  termsVersions: ContractTerms[];
  projects: ProjectSummary[];
  /** Customer people authorised to order, cancel and approve Additional PAD. */
  representatives: AuthorisedRepresentative[];
}

export interface AuthorisedRepresentative {
  name: string;
  email: string | null;
  phone: string | null;
  authority: string | null;
}

/** A contract's one-off dated amount (milestone, instalment, set-up fee). */
export interface ScheduledCharge {
  id: string;
  contractId: string;
  /** null = billed on the contract's default project — see billedProjectId. */
  projectId: string | null;
  projectName: string | null;
  billedProjectId: string | null;
  date: string;
  amount: number;
  description: string;
  /** On a specification beyond draft — frozen. */
  invoiced: boolean;
}

export interface ScheduledChargeRequestBody {
  projectId: string | null;
  date: string;
  amount: number;
  description: string;
}

/** GET /api/contracts/{id}/visits row. */
export interface ContractVisit {
  periodId: string;
  number: number;
  withinLimit: boolean;
  projectId: string;
  projectName: string;
  resourceId: string;
  resourceName: string;
  workPackageId: string;
  workPackageName: string;
  startDate: string;
  endDate: string;
}

export interface ContractVisits {
  used: number;
  /** null = no limit configured. */
  included: number | null;
  remaining: number | null;
  visits: ContractVisit[];
}

/** PAD rules of a terms version (all switchable per contract). */
export interface PadRules {
  carryOverBasis: CarryOverBasis;
  carryOverRoundUpTo: number;
  padInvoicing: PadInvoicing;
  additionalPadRate: number | null;
  sharedDayMinimum: SharedDayMinimum;
  unbookedTimeConsumesPad: boolean;
  unbookedTimeIncrementMinutes: number;
  /** "HH:mm:ss" */
  defaultStartTime: string;
  startTimeNoticeBusinessDays: number;
}

export interface ContractRequestBody {
  customerId: string;
  endClientId: string | null;
  endClientReference: string | null;
  name: string;
  externalReference: string | null;
  startDate: string;
  endDate: string | null;
  isActive: boolean;
  representatives: AuthorisedRepresentative[];
}

export interface ContractTerms {
  id: string;
  contractId: string;
  version: number;
  effectiveFrom: string;
  createdUtc: string;
  currency: string;
  // ── Billing components ──
  timeBilling: TimeBilling;
  padEnabled: boolean;
  padRate: number | null;
  hourlyRate: number | null;
  saturdayRate: number | null;
  sundayRate: number | null;
  holidayRate: number | null;
  overtimeAfterHours: number;
  overtimePercent: number;
  mobilisationFee: number | null;
  expenseMarkupPercent: number;
  minBillableHoursPerDay: number | null;
  dayRate: number | null;
  saturdayDayRate: number | null;
  sundayDayRate: number | null;
  holidayDayRate: number | null;
  halfDayRate: number | null;
  halfDayMaxHours: number | null;
  padHoursPerDay: number;
  minPadPerBookedDay: number;
  carryOverMaxPercent: number;
  carryOverMonths: number;
  cancellationDeadlineBusinessDays: number;
  cancellationSteps: CancellationStep[];
  holidayCalendar: string;
  travelExpensePolicy: string | null;
  indexation: string | null;
  notes: string | null;
  /** Mobilisations to visit-counted packages included in the price; null = no limit. */
  includedVisits: number | null;
  padRules: PadRules | null;
}

export interface CancellationStep {
  minBusinessDaysBefore: number;
  chargeHours: number | null;
  chargeAmount: number | null;
  /** Share of the day rate charged (0–1) — day-rate contracts. */
  chargeDayFraction?: number | null;
}

export interface ContractTermsRequestBody {
  effectiveFrom: string;
  currency: string;
  timeBilling: TimeBilling;
  padEnabled: boolean;
  padRate: number | null;
  hourlyRate: number | null;
  saturdayRate: number | null;
  sundayRate: number | null;
  holidayRate: number | null;
  overtimeAfterHours: number;
  overtimePercent: number;
  mobilisationFee: number | null;
  expenseMarkupPercent: number;
  minBillableHoursPerDay: number | null;
  dayRate: number | null;
  saturdayDayRate: number | null;
  sundayDayRate: number | null;
  holidayDayRate: number | null;
  halfDayRate: number | null;
  halfDayMaxHours: number | null;
  padHoursPerDay: number;
  minPadPerBookedDay: number;
  carryOverMaxPercent: number;
  carryOverMonths: number;
  cancellationDeadlineBusinessDays: number;
  cancellationSteps: CancellationStep[];
  holidayCalendar: string;
  travelExpensePolicy: string | null;
  indexation: string | null;
  notes: string | null;
  includedVisits: number | null;
  // ── PAD rules (top-level on the request) ──
  carryOverBasis: CarryOverBasis;
  carryOverRoundUpTo: number;
  padInvoicing: PadInvoicing;
  additionalPadRate: number | null;
  sharedDayMinimum: SharedDayMinimum;
  unbookedTimeConsumesPad: boolean;
  unbookedTimeIncrementMinutes: number;
  /** "HH:mm" */
  defaultStartTime: string;
  startTimeNoticeBusinessDays: number;
}

export interface ProjectSummary {
  id: string;
  contractId: string;
  name: string;
  code: string | null;
  isActive: boolean;
}

export interface ProjectDetail {
  id: string;
  contractId: string;
  contractName: string;
  customerName: string;
  name: string;
  code: string | null;
  description: string | null;
  isActive: boolean;
  workPackages: WorkPackage[];
  estimatedQuantities: EstimatedQuantity[];
  /** The project's contracted total baseline in PAD (null = not tracked). */
  contractedBaselinePad: number | null;
}

export interface ProjectRequestBody {
  contractId: string;
  name: string;
  code: string | null;
  description: string | null;
  isActive: boolean;
  contractedBaselinePad?: number | null;
}

export interface WorkPackage {
  id: string;
  projectId: string;
  name: string;
  notes: string | null;
  infoItems: WorkPackageInfoItem[];
  /** contract = follows the contract (e.g. PAD); outsideContract = billed by time, no PAD. */
  pricingMode: WorkPackagePricing;
  /** Mobilisations here count against the contract's included visits. */
  countsAsVisit: boolean;
}

/** One typed information field on a work package ("Address", "Access"…). */
export interface WorkPackageInfoItem {
  id: string;
  type: string;
  description: string;
}

export interface WorkPackageRequestBody {
  projectId: string;
  name: string;
  notes: string | null;
  infoItems?: { type: string; description: string }[];
  pricingMode?: WorkPackagePricing;
  countsAsVisit?: boolean;
}

// One versioned package-rate override row — set fields win over contract terms,
// null fields inherit (GET/POST /api/work-packages/{id}/rates).
// resourceId set = resource×package matrix cell; null = package-wide default.
export interface WorkPackageRate {
  id: string;
  workPackageId: string;
  effectiveFrom: string;
  resourceId: string | null;
  resourceName: string | null;
  needsReview: boolean;
  hourlyRate: number | null;
  saturdayRate: number | null;
  sundayRate: number | null;
  holidayRate: number | null;
  dayRate: number | null;
  saturdayDayRate: number | null;
  sundayDayRate: number | null;
  holidayDayRate: number | null;
  halfDayRate: number | null;
  halfDayMaxHours: number | null;
  mobilisationFee: number | null;
  notes: string | null;
}

export interface WorkPackageRateRequestBody {
  resourceId?: string | null;
  effectiveFrom: string;
  hourlyRate?: number | null;
  saturdayRate?: number | null;
  sundayRate?: number | null;
  holidayRate?: number | null;
  dayRate?: number | null;
  saturdayDayRate?: number | null;
  sundayDayRate?: number | null;
  holidayDayRate?: number | null;
  halfDayRate?: number | null;
  halfDayMaxHours?: number | null;
  mobilisationFee?: number | null;
  notes?: string | null;
}

// A resource allowed to self-report on a project (access-list row).
export interface ProjectAccess {
  resourceId: string;
  resourceName: string;
  createdUtc: string;
}

// A resource staffed on a project — the commercial roster (GET
// /api/projects/{id}/resources). pendingReviews counts inherited rate rows
// not yet reviewed on the project's work packages.
export interface ProjectResource {
  resourceId: string;
  resourceName: string;
  resourceType: string;
  addedUtc: string;
  pendingReviews: number;
}

export interface EstimatedQuantity {
  id: string;
  projectId: string;
  label: string;
  quantity: number;
  unit: string;
  notes: string | null;
}

export interface EstimatedQuantityRequestBody {
  projectId: string;
  label: string;
  quantity: number;
  unit: string;
  notes: string | null;
}

export interface ResourceSummary {
  id: string;
  displayName: string;
  type: ResourceType;
  isActive: boolean;
  userId: string | null;
  /** Any competency record due or expired — flag for renewal. */
  competencyWarning: boolean;
}

export interface ResourceDetail {
  id: string;
  displayName: string;
  type: ResourceType;
  email: string | null;
  phone: string | null;
  competencies: Competency[];
  isActive: boolean;
  userId: string | null;
  leave: ResourceLeave[];
}

/** One competency/certification record — status computed server-side. */
export interface Competency {
  id: string;
  label: string;
  expiryDate: string | null;
  reminderDays: number | null;
  status: 'unbounded' | 'valid' | 'due' | 'expired';
}

export interface CompetencyRequestBody {
  label: string;
  expiryDate: string | null;
  reminderDays: number | null;
}

export interface ResourceRequestBody {
  displayName: string;
  type: ResourceType;
  email: string | null;
  phone: string | null;
  userId: string | null;
  isActive: boolean;
}

export interface ResourceLeave {
  id: string;
  resourceId: string;
  from: string;
  to: string;
  type: LeaveType;
  notes: string | null;
}

export interface ResourceLeaveRequestBody {
  from: string;
  to: string;
  type: LeaveType;
  notes: string | null;
}

export interface CompensationAgreement {
  id: string;
  resourceId: string;
  contractId: string | null;
  contractName: string | null;
  workPackageId: string | null;
  workPackageName: string | null;
  workPackageProjectName: string | null;
  version: number;
  effectiveFrom: string;
  createdUtc: string;
  currency: string;
  hourlyRate: number | null;
  saturdayRate: number | null;
  sundayRate: number | null;
  holidayRate: number | null;
  overtimeAfterHours: number;
  overtimePercent: number;
  travelTimePercent: number | null;
  mileageRate: number | null;
  perDiem: PerDiemPolicy;
  perDiemAmount: number | null;
  expensePolicy: string | null;
  notes: string | null;
}

// ── Bookings & PAD ──────────────────────────────────────────────────────────

export type BookingStatus =
  | 'forecast' | 'tentative' | 'requested' | 'confirmed' | 'acknowledged'
  | 'inProgress' | 'completed' | 'cancelled' | 'rescheduled';

export interface Booking {
  id: string;
  projectId: string;
  projectName: string;
  customerName: string;
  resourceId: string;
  resourceName: string;
  workPackageId: string | null;
  workPackageName: string | null;
  date: string;
  plannedHours: number;
  activityRef: string | null;
  workDescription: string | null;
  customerContact: string | null;
  evidenceRef: string | null;
  status: BookingStatus;
  expectedPad: number | null;
  acknowledgedUtc: string | null;
  replacesBookingId: string | null;
  rescheduledToBookingId: string | null;
  warnings: string[];
  /** Who at the customer requested/confirmed the day (evidence, §5). */
  instructedBy?: string | null;
  instructedAtUtc?: string | null;
  /** Project split of a shared booked day (empty = single project). */
  shares?: BookingShare[] | null;
  /** Agreed start time "HH:mm:ss" (null = none agreed). */
  startTime?: string | null;
  /** When working time commences per the notice rule. */
  effectiveStartTime?: string | null;
}

/** One project's part of a shared booked day; expectedPad null for resources. */
export interface BookingShare {
  projectId: string;
  projectName: string;
  plannedHours: number;
  expectedPad: number | null;
}

export interface BookingShareRequest {
  projectId: string;
  plannedHours: number;
}

export interface BookingRequestBody {
  projectId: string;
  resourceId: string;
  workPackageId: string | null;
  date: string;
  plannedHours: number;
  activityRef: string | null;
  workDescription: string | null;
  customerContact: string | null;
  evidenceRef: string | null;
  status: 'forecast' | 'tentative' | 'requested';
  instructedBy?: string | null;
  instructedAtUtc?: string | null;
  shares?: BookingShareRequest[];
  startTime?: string | null;
  /** Update only: remove the agreed start time. */
  clearStartTime?: boolean;
}

export type PadTransactionType =
  | 'baselineSet' | 'carryOverIn' | 'commit' | 'release' | 'utilise'
  | 'additionalApproved' | 'autoAdditional' | 'borrowNextMonth'
  | 'carryOverOut' | 'expire' | 'adjustment';

export interface PadTransaction {
  id: string;
  type: PadTransactionType;
  amount: number;
  bookingId: string | null;
  note: string | null;
  createdUtc: string;
}

export interface PadDashboard {
  projectId: string;
  projectName: string;
  year: number;
  month: number;
  baseline: number;
  carryOverIn: number;
  additionalApproved: number;
  committedOpen: number;
  utilised: number;
  released: number;
  expired: number;
  carryOverOut: number;
  borrowedFromThisMonth: number;
  adjustments: number;
  totalAvailable: number;
  free: number;
  closed: boolean;
  closedUtc: string | null;
  transactions: PadTransaction[];
}

export interface CloseMonthResult {
  baseline: number;
  utilised: number;
  releasedOpenCommitments: number;
  carryOverOut: number;
  expired: number;
  carryOverInNextMonth: number;
  nextYear: number;
  nextMonth: number;
}

export type AdditionalPadStatus = 'draft' | 'submitted' | 'approved' | 'rejected' | 'withdrawn';

export interface AdditionalPadRequest {
  id: string;
  projectId: string;
  year: number;
  month: number;
  quantity: number;
  rate: number | null;
  reason: string | null;
  reference: string | null;
  status: AdditionalPadStatus;
  createdUtc: string;
  decidedUtc: string | null;
  /** The prepared booking waiting on this request, if any. */
  bookingId: string | null;
  /** The customer representative who approved it (evidence, §14). */
  approvedByRepresentative?: string | null;
}

// ── Time reporting (execution ledger) ───────────────────────────────────────

export type TimeEntryCategory = 'work' | 'waiting' | 'standby' | 'break';
export type TravelMode = 'car' | 'train' | 'flight' | 'boat' | 'other';
export type ExpenseCategory = 'travel' | 'meal' | 'accommodation' | 'material' | 'equipment' | 'other';
export type TimesheetStatus = 'draft' | 'submitted' | 'approved' | 'returned' | 'rejected' | 'invoiced';
// How a reported row is billed: 'contract' follows the agreement model
// (day rate / PAD / hourly), 'hours' always bills plain hourly.
export type EntryBilling = 'contract' | 'hours';

export interface TimeEntry {
  id: string;
  resourceId: string;
  resourceName: string;
  projectId: string;
  projectName: string;
  bookingId: string | null;
  workPackageId: string | null;
  workPackageName: string | null;
  date: string;
  category: TimeEntryCategory;
  actualHours: number;
  billing: EntryBilling;
  description: string | null;
  payableHours: number | null;
  billableHours: number | null;
  minimumAdjustmentHours: number | null;
  timesheetId: string | null;
  timesheetStatus: TimesheetStatus | null;
  /** Customer-side minimum adjustment — null for Resource callers. */
  billableMinimumAdjustmentHours?: number | null;
}

export interface TimeEntryRequestBody {
  resourceId?: string | null;
  projectId: string;
  bookingId?: string | null;
  workPackageId?: string | null;
  date: string;
  category: TimeEntryCategory;
  actualHours: number;
  billing?: EntryBilling;
  description?: string | null;
}

export interface TravelRecord {
  id: string;
  resourceId: string;
  resourceName: string;
  projectId: string | null;
  projectName: string | null;
  bookingId: string | null;
  date: string;
  fromPlace: string | null;
  toPlace: string | null;
  mode: TravelMode;
  distanceKm: number | null;
  durationHours: number | null;
  mobilisationPeriodId: string | null;
  notes: string | null;
  timesheetId: string | null;
}

export interface TravelRecordRequestBody {
  resourceId?: string | null;
  projectId?: string | null;
  bookingId?: string | null;
  date: string;
  fromPlace?: string | null;
  toPlace?: string | null;
  mode: TravelMode;
  distanceKm?: number | null;
  durationHours?: number | null;
  notes?: string | null;
}

export interface Expense {
  id: string;
  resourceId: string;
  resourceName: string;
  projectId: string | null;
  projectName: string | null;
  bookingId: string | null;
  date: string;
  category: ExpenseCategory;
  amount: number;
  currency: string;
  description: string | null;
  receiptRef: string | null;
  reimbursableToResource: boolean;
  rechargeableToCustomer: boolean;
  includedInRate: boolean;
  timesheetId: string | null;
}

export interface ExpenseRequestBody {
  resourceId?: string | null;
  projectId?: string | null;
  bookingId?: string | null;
  date: string;
  category: ExpenseCategory;
  amount: number;
  currency: string;
  description?: string | null;
  receiptRef?: string | null;
  reimbursableToResource: boolean;
  rechargeableToCustomer: boolean;
  includedInRate: boolean;
  /** Save despite a same-day/amount/currency look-alike (409 possibleDuplicate). */
  confirmDuplicate?: boolean;
}

export interface AllowanceClaim {
  id: string;
  resourceId: string;
  resourceName: string;
  projectId: string | null;
  projectName: string | null;
  date: string;
  amount: number;
  currency: string;
  notes: string | null;
  timesheetId: string | null;
}

export interface AllowanceRequestBody {
  resourceId?: string | null;
  projectId?: string | null;
  date: string;
  amount: number;
  currency: string;
  notes?: string | null;
}

export interface MobilisationPeriod {
  id: string;
  projectId: string;
  projectName: string;
  resourceId: string;
  resourceName: string;
  startDate: string;
  endDate: string;
  confirmedUtc: string;
  workPackageId: string | null;
  workPackageName: string | null;
  /** Place in the contract's visit count (null = not a visit-counted package). */
  visitNumber?: number | null;
  includedVisits?: number | null;
  /** Beyond the included visits: travel time bills and travel costs recharge. */
  additionalTravel?: boolean;
}

export interface MobilisationSuggestion {
  projectId: string;
  projectName: string;
  startDate: string;
  endDate: string;
}

export interface Timesheet {
  id: string;
  resourceId: string;
  resourceName: string;
  year: number;
  isoWeek: number;
  status: TimesheetStatus;
  submittedUtc: string | null;
  reviewedUtc: string | null;
  reviewerNote: string | null;
  entryCount: number;
  travelCount: number;
  expenseCount: number;
  allowanceCount: number;
  totalActualHours: number;
}

export type TimesheetReviewAction = 'submitted' | 'approved' | 'returned' | 'rejected' | 'reopened';

/** One step of the review history, oldest first. */
export interface TimesheetReview {
  action: TimesheetReviewAction;
  note: string | null;
  byUserId: string | null;
  byName: string | null;
  atUtc: string;
}

export interface TimesheetDetail {
  sheet: Timesheet;
  entries: TimeEntry[];
  travel: TravelRecord[];
  expenses: Expense[];
  allowances: AllowanceClaim[];
  reviews?: TimesheetReview[];
}

// ── Documents (receipts, booking evidence) ──────────────────────────────────

export type DocumentOwnerType = 'expense' | 'booking';

export interface DocumentAttachment {
  id: string;
  ownerType: DocumentOwnerType;
  ownerId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  label: string | null;
  createdUtc: string;
}

// ── Billing (customer invoices + subcontractor statements) ──────────────────

export type InvoiceStatus = 'draft' | 'customerReview' | 'final' | 'issued' | 'paid';
export type StatementStatus = 'draft' | 'final' | 'paid';
export type InvoiceLineKind =
  | 'hours' | 'expense' | 'mobilisation' | 'pad' | 'additionalPad' | 'autoAdditionalPad'
  | 'fixedPrice' | 'scheduledCharge' | 'travel' | 'cancellation' | 'manual';
export type StatementLineKind = 'hours' | 'expense' | 'allowance' | 'mileage' | 'travelTime' | 'manual';

export interface InvoiceSpecification {
  id: string;
  projectId: string;
  projectName: string;
  /** The contract party the spec is addressed to. */
  customerName: string;
  /** Whom the work was for when an intermediary holds the contract. */
  endClientName: string | null;
  endClientReference: string | null;
  year: number;
  month: number;
  /** 0 = the month's first spec; supplements count up. */
  revision: number;
  status: InvoiceStatus;
  currency: string;
  totalAmount: number;
  lineCount: number;
  createdUtc: string;
  issuedUtc: string | null;
  issuedByUserId: string | null;
  paidUtc: string | null;
  paidByUserId: string | null;
  warnings: string[];
}

/** Approved rows of a month no specification billed yet (/api/invoices/unbilled). */
export interface UnbilledRow {
  projectId: string;
  projectName: string;
  year: number;
  month: number;
  entryCount: number;
  hours: number;
  expenseCount: number;
  hasFrozenSpec: boolean;
}

export interface BillingLine {
  id: string;
  kind: string;
  date: string | null;
  description: string;
  explanation: string | null;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface InvoiceDetail {
  invoice: InvoiceSpecification;
  lines: BillingLine[];
}

export interface SubcontractorStatement {
  id: string;
  resourceId: string;
  resourceName: string;
  year: number;
  month: number;
  status: StatementStatus;
  currency: string;
  totalAmount: number;
  lineCount: number;
  createdUtc: string;
  paidUtc: string | null;
  paidByUserId: string | null;
  warnings: string[];
}

export interface StatementDetail {
  statement: SubcontractorStatement;
  lines: BillingLine[];
}

export type SupplierInvoiceStatus = 'registered' | 'matched' | 'discrepancy' | 'approved' | 'paid';

export interface SupplierInvoice {
  id: string;
  resourceId: string;
  resourceName: string;
  number: string;
  invoiceDate: string;
  dueDate: string | null;
  currency: string;
  totalAmount: number;
  status: SupplierInvoiceStatus;
  statementId: string | null;
  paidUtc: string | null;
  approvalNote: string | null;
}

export interface SupplierInvoiceLine {
  id: string;
  kind: StatementLineKind;
  description: string;
  amount: number;
}

export interface MatchDiff {
  kind: string;
  invoiced: number;
  statement: number;
  diff: number;
}

export interface SupplierInvoiceDetail {
  invoice: SupplierInvoice;
  lines: SupplierInvoiceLine[];
  diffs: MatchDiff[] | null;
}

export interface CostBreakdown {
  currency: string;
  hours: number;
  expenses: number;
  allowances: number;
  mileage: number;
  travelTime: number;
  total: number;
}

/** One row per project and currency — amounts never mix currencies. */
export interface MarginRow {
  projectId: string;
  projectName: string;
  customerName: string;
  /** Effective end client — the contract's end client, or the party itself
   * on a direct contract. */
  endClientId: string;
  endClientName: string;
  currency: string;
  billed: number;
  cost: number;
  margin: number;
  marginPercent: number | null;
  costBreakdown: CostBreakdown | null;
}

// ── Baseline schedule (GET/PUT /api/projects/{id}/pad/baseline-schedule) ────

export interface BaselineMonth {
  year: number;
  month: number;
  amount: number;
}

/** difference = contracted total − scheduled sum (null without a total). */
export interface BaselineSchedule {
  months: BaselineMonth[];
  scheduled: number;
  contractedTotal: number | null;
  difference: number | null;
  warning: string | null;
}
