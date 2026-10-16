// Mirrors the API DTOs in ContractFlow.Api/Models/Dto (camelCase JSON).

/** Billing-component keys on the terms version in force: 'hourly' |
 * 'dailyRate' | 'pad' | 'fixedPrice'. A contract may carry several at once. */
export type ContractComponent = 'hourly' | 'dailyRate' | 'pad' | 'fixedPrice';
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
  contractCount: number;
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

export interface EndClient {
  id: string;
  name: string;
  country: string | null;
  addressLine: string | null;
  postalCode: string | null;
  city: string | null;
  notes: string | null;
}

export interface EndClientRequestBody {
  name: string;
  country: string | null;
  addressLine: string | null;
  postalCode: string | null;
  city: string | null;
  notes: string | null;
}

/** GET /api/meta/{calendars,countries,currencies} row — code + English name. */
export interface MetaEntry {
  code: string;
  name: string;
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
  endClientId: string | null;
  endClientName: string | null;
  name: string;
  externalReference: string | null;
  components: ContractComponent[];
  startDate: string;
  endDate: string | null;
  isActive: boolean;
  termsVersions: ContractTerms[];
  projects: ProjectSummary[];
}

export interface ContractRequestBody {
  customerId: string;
  endClientId: string | null;
  name: string;
  externalReference: string | null;
  startDate: string;
  endDate: string | null;
  isActive: boolean;
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
  fixedPriceEnabled: boolean;
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
  fixedPriceAmount: number | null;
  fixedPriceDate: string | null;
  fixedPriceNote: string | null;
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
}

export interface CancellationStep {
  minBusinessDaysBefore: number;
  chargeHours: number | null;
  chargeAmount: number | null;
}

export interface ContractTermsRequestBody {
  effectiveFrom: string;
  currency: string;
  timeBilling: TimeBilling;
  padEnabled: boolean;
  fixedPriceEnabled: boolean;
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
  fixedPriceAmount: number | null;
  fixedPriceDate: string | null;
  fixedPriceNote: string | null;
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
}

export interface ProjectRequestBody {
  contractId: string;
  name: string;
  code: string | null;
  description: string | null;
  isActive: boolean;
}

export interface WorkPackage {
  id: string;
  projectId: string;
  name: string;
  address: string | null;
  notes: string | null;
}

export interface WorkPackageRequestBody {
  projectId: string;
  name: string;
  address: string | null;
  notes: string | null;
}

// One versioned site-rate override row — set fields win over contract terms,
// null fields inherit (GET/POST /api/work-packages/{id}/rates).
export interface WorkPackageRate {
  id: string;
  workPackageId: string;
  effectiveFrom: string;
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
}

export interface ResourceDetail {
  id: string;
  displayName: string;
  type: ResourceType;
  email: string | null;
  phone: string | null;
  competencies: string | null;
  isActive: boolean;
  userId: string | null;
  leave: ResourceLeave[];
}

export interface ResourceRequestBody {
  displayName: string;
  type: ResourceType;
  email: string | null;
  phone: string | null;
  competencies: string | null;
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
}

// ── Time reporting (execution ledger) ───────────────────────────────────────

export type TimeEntryCategory = 'work' | 'waiting' | 'standby' | 'break';
export type TravelMode = 'car' | 'train' | 'flight' | 'boat' | 'other';
export type ExpenseCategory = 'travel' | 'meal' | 'accommodation' | 'material' | 'equipment' | 'other';
export type TimesheetStatus = 'draft' | 'submitted' | 'approved' | 'returned' | 'rejected';
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
}

export interface TimeEntryRequestBody {
  resourceId?: string | null;
  projectId: string;
  bookingId?: string | null;
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

export interface TimesheetDetail {
  sheet: Timesheet;
  entries: TimeEntry[];
  travel: TravelRecord[];
  expenses: Expense[];
  allowances: AllowanceClaim[];
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
export type InvoiceLineKind = 'hours' | 'expense' | 'mobilisation' | 'manual';
export type StatementLineKind = 'hours' | 'expense' | 'allowance' | 'mileage' | 'travelTime' | 'manual';

export interface InvoiceSpecification {
  id: string;
  projectId: string;
  projectName: string;
  customerName: string;
  year: number;
  month: number;
  status: InvoiceStatus;
  currency: string;
  totalAmount: number;
  lineCount: number;
  createdUtc: string;
  issuedUtc: string | null;
  paidUtc: string | null;
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

export interface MarginRow {
  projectId: string;
  projectName: string;
  customerName: string;
  billed: number;
  cost: number;
  margin: number;
  marginPercent: number | null;
}
