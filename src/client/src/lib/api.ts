import { DEV_USER_HEADER, getDevUserId } from './currentUser';
import type {
  AdditionalPadRequest,
  AllowanceClaim,
  AllowanceRequestBody,
  Booking,
  BookingRequestBody,
  CloseMonthResult,
  DocumentAttachment,
  DocumentOwnerType,
  CompensationAgreement,
  ContractDetail,
  ContractRequestBody,
  ContractSummary,
  ContractTerms,
  ContractTermsRequestBody,
  CustomerDetail,
  CustomerRequestBody,
  CustomerSummary,
  EndClient,
  EndClientRequestBody,
  EstimatedQuantity,
  EstimatedQuantityRequestBody,
  Expense,
  ExpenseRequestBody,
  InvoiceDetail,
  InvoiceSpecification,
  Location,
  LocationRequestBody,
  MarginRow,
  MobilisationPeriod,
  MobilisationSuggestion,
  PadDashboard,
  PadTransaction,
  ProjectDetail,
  ProjectRequestBody,
  ProjectSummary,
  ResourceDetail,
  ResourceLeave,
  ResourceLeaveRequestBody,
  ResourceRequestBody,
  ResourceSummary,
  StatementDetail,
  SubcontractorStatement,
  SupplierInvoice,
  SupplierInvoiceDetail,
  TimeEntry,
  TimeEntryRequestBody,
  Timesheet,
  TimesheetDetail,
  TimesheetStatus,
  TravelRecord,
  TravelRecordRequestBody,
} from './types';

function devHeaders(): HeadersInit {
  const id = getDevUserId();
  return id ? { [DEV_USER_HEADER]: id } : {};
}

export async function apiGet<T>(path: string): Promise<T> {
  const res = await fetch(path, { headers: { ...devHeaders() } });
  if (!res.ok) throw new Error(await res.text());
  return (await res.json()) as T;
}

async function apiSend<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: { 'Content-Type': 'application/json', ...devHeaders() },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) throw new Error(await res.text());
  return res.status === 204 ? (undefined as T) : ((await res.json()) as T);
}

export const apiPost = <T>(path: string, body?: unknown) => apiSend<T>('POST', path, body);
export const apiPut = <T>(path: string, body: unknown) => apiSend<T>('PUT', path, body);
export const apiDelete = (path: string) => apiSend<void>('DELETE', path);

/** Multipart upload — the browser sets the Content-Type boundary itself. */
async function apiUpload<T>(path: string, file: File, label?: string): Promise<T> {
  const form = new FormData();
  form.append('file', file);
  if (label) form.append('label', label);
  const res = await fetch(path, { method: 'POST', headers: { ...devHeaders() }, body: form });
  if (!res.ok) throw new Error(await res.text());
  return (await res.json()) as T;
}

/** Extracts a readable message from an API error body ({error, message}). */
export function errorMessage(e: unknown): string {
  const raw = e instanceof Error ? e.message : String(e);
  try {
    const parsed = JSON.parse(raw) as { message?: string; error?: string };
    return parsed.message ?? parsed.error ?? raw;
  } catch {
    return raw;
  }
}

export interface UserDto {
  id: string;
  displayName: string;
  email: string | null;
  role: string;
}

export const getMe = () => apiGet<UserDto>('/api/me');
export const getUsers = () => apiGet<UserDto[]>('/api/users');

// ── Commercial structure ────────────────────────────────────────────────────

export const getCustomers = () => apiGet<CustomerSummary[]>('/api/customers');
export const getCustomer = (id: string) => apiGet<CustomerDetail>(`/api/customers/${id}`);
export const createCustomer = (b: CustomerRequestBody) => apiPost<CustomerSummary>('/api/customers', b);
export const updateCustomer = (id: string, b: CustomerRequestBody) =>
  apiPut<CustomerSummary>(`/api/customers/${id}`, b);
export const deleteCustomer = (id: string) => apiDelete(`/api/customers/${id}`);

export const getEndClients = () => apiGet<EndClient[]>('/api/endclients');
export const createEndClient = (b: EndClientRequestBody) => apiPost<EndClient>('/api/endclients', b);
export const updateEndClient = (id: string, b: EndClientRequestBody) =>
  apiPut<EndClient>(`/api/endclients/${id}`, b);
export const deleteEndClient = (id: string) => apiDelete(`/api/endclients/${id}`);

export const getContracts = (customerId?: string) =>
  apiGet<ContractSummary[]>(`/api/contracts${customerId ? `?customerId=${customerId}` : ''}`);
export const getContract = (id: string) => apiGet<ContractDetail>(`/api/contracts/${id}`);
export const createContract = (b: ContractRequestBody) => apiPost<ContractSummary>('/api/contracts', b);
export const updateContract = (id: string, b: ContractRequestBody) =>
  apiPut<ContractSummary>(`/api/contracts/${id}`, b);
export const deleteContract = (id: string) => apiDelete(`/api/contracts/${id}`);
export const getContractTerms = (contractId: string) =>
  apiGet<ContractTerms[]>(`/api/contracts/${contractId}/terms`);
export const getEffectiveTerms = (contractId: string, on?: string) =>
  apiGet<ContractTerms>(`/api/contracts/${contractId}/terms/effective${on ? `?on=${on}` : ''}`);
export const createContractTerms = (contractId: string, b: ContractTermsRequestBody) =>
  apiPost<ContractTerms>(`/api/contracts/${contractId}/terms`, b);
export const deleteContractTerms = (contractId: string, termId: string) =>
  apiDelete(`/api/contracts/${contractId}/terms/${termId}`);

export const getProjects = (contractId?: string) =>
  apiGet<ProjectSummary[]>(`/api/projects${contractId ? `?contractId=${contractId}` : ''}`);
export const getProject = (id: string) => apiGet<ProjectDetail>(`/api/projects/${id}`);
export const createProject = (b: ProjectRequestBody) => apiPost<ProjectSummary>('/api/projects', b);
export const updateProject = (id: string, b: ProjectRequestBody) =>
  apiPut<ProjectSummary>(`/api/projects/${id}`, b);
export const deleteProject = (id: string) => apiDelete(`/api/projects/${id}`);

export const createLocation = (b: LocationRequestBody) => apiPost<Location>('/api/locations', b);
export const updateLocation = (id: string, b: LocationRequestBody) =>
  apiPut<Location>(`/api/locations/${id}`, b);
export const deleteLocation = (id: string) => apiDelete(`/api/locations/${id}`);

export const createQuantity = (b: EstimatedQuantityRequestBody) =>
  apiPost<EstimatedQuantity>('/api/quantities', b);
export const updateQuantity = (id: string, b: EstimatedQuantityRequestBody) =>
  apiPut<EstimatedQuantity>(`/api/quantities/${id}`, b);
export const deleteQuantity = (id: string) => apiDelete(`/api/quantities/${id}`);

// ── Resources ───────────────────────────────────────────────────────────────

export const getResources = () => apiGet<ResourceSummary[]>('/api/resources');
export const getResource = (id: string) => apiGet<ResourceDetail>(`/api/resources/${id}`);
export const createResource = (b: ResourceRequestBody) => apiPost<ResourceDetail>('/api/resources', b);
export const updateResource = (id: string, b: ResourceRequestBody) =>
  apiPut<ResourceDetail>(`/api/resources/${id}`, b);
export const deleteResource = (id: string) => apiDelete(`/api/resources/${id}`);
export const getAgreements = (resourceId: string) =>
  apiGet<CompensationAgreement[]>(`/api/resources/${resourceId}/agreements`);
export const createAgreement = (resourceId: string, b: AgreementRequestBody) =>
  apiPost<CompensationAgreement>(`/api/resources/${resourceId}/agreements`, b);
export const deleteAgreement = (resourceId: string, agreementId: string) =>
  apiDelete(`/api/resources/${resourceId}/agreements/${agreementId}`);
export const createLeave = (resourceId: string, b: ResourceLeaveRequestBody) =>
  apiPost<ResourceLeave>(`/api/resources/${resourceId}/leave`, b);
export const deleteLeave = (resourceId: string, leaveId: string) =>
  apiDelete(`/api/resources/${resourceId}/leave/${leaveId}`);

// ── Bookings ────────────────────────────────────────────────────────────────

export const getBookings = (q: { from?: string; to?: string; projectId?: string; resourceId?: string }) => {
  const p = new URLSearchParams();
  if (q.from) p.set('from', q.from);
  if (q.to) p.set('to', q.to);
  if (q.projectId) p.set('projectId', q.projectId);
  if (q.resourceId) p.set('resourceId', q.resourceId);
  const s = p.toString();
  return apiGet<Booking[]>(`/api/bookings${s ? `?${s}` : ''}`);
};
export const createBooking = (b: BookingRequestBody) => apiPost<Booking>('/api/bookings', b);
export const updateBooking = (id: string, b: Partial<BookingRequestBody>) =>
  apiPut<Booking>(`/api/bookings/${id}`, b);
export const bookingAction = (id: string, action: 'confirm' | 'acknowledge' | 'start' | 'complete' | 'cancel') =>
  apiPost<unknown>(`/api/bookings/${id}/${action}`, {});
export const rescheduleBooking = (id: string, date: string, plannedHours?: number) =>
  apiPost<{ original: Booking; replacement: Booking; replacementConfirmed: boolean; padEffect: string }>(
    `/api/bookings/${id}/reschedule`, { date, plannedHours });
export const deleteBooking = (id: string) => apiDelete(`/api/bookings/${id}`);

// ── PAD ─────────────────────────────────────────────────────────────────────

export const getPadDashboard = (projectId: string, year: number, month: number) =>
  apiGet<PadDashboard>(`/api/projects/${projectId}/pad/${year}/${month}`);
export const setBaseline = (projectId: string, year: number, month: number, amount: number, note?: string) =>
  apiPost<PadTransaction>(`/api/projects/${projectId}/pad/${year}/${month}/baseline`, { amount, note });
export const closeMonth = (projectId: string, year: number, month: number) =>
  apiPost<CloseMonthResult>(`/api/projects/${projectId}/pad/${year}/${month}/close`, {});
export const getPadRequests = (projectId: string, year?: number, month?: number) => {
  const p = new URLSearchParams({ projectId });
  if (year) p.set('year', String(year));
  if (month) p.set('month', String(month));
  return apiGet<AdditionalPadRequest[]>(`/api/pad-requests?${p}`);
};
export const createPadRequest = (b: {
  projectId: string; year: number; month: number; quantity: number;
  rate?: number | null; reason?: string | null; reference?: string | null;
}) => apiPost<AdditionalPadRequest>('/api/pad-requests', b);
export const padRequestAction = (id: string, action: 'approve' | 'reject' | 'withdraw') =>
  apiPost<AdditionalPadRequest>(`/api/pad-requests/${id}/${action}`, {});
export const deletePadRequest = (id: string) => apiDelete(`/api/pad-requests/${id}`);

// ── Calendar feed (.ics) ────────────────────────────────────────────────────

export const getFeedUrl = (resourceId?: string) =>
  apiGet<{ url: string }>(`/api/calendar/feed-url${resourceId ? `?resourceId=${resourceId}` : ''}`);
export const rotateFeedUrl = (resourceId?: string) =>
  apiPost<{ url: string }>(`/api/calendar/feed-url/rotate${resourceId ? `?resourceId=${resourceId}` : ''}`, {});

export interface AgreementRequestBody {
  contractId: string | null;
  effectiveFrom: string;
  currency: string;
  hourlyRate: number | null;
  saturdayRate: number | null;
  sundayRate: number | null;
  holidayRate: number | null;
  overtimeAfterHours: number;
  overtimePercent: number;
  travelTimePercent: number | null;
  mileageRate: number | null;
  perDiem: PerDiemPolicyValue;
  perDiemAmount: number | null;
  expensePolicy: string | null;
  notes: string | null;
}

export type PerDiemPolicyValue = 'none' | 'statutory' | 'custom';

// ── Time reporting (Phase 2) ────────────────────────────────────────────────

export const getTimeEntries = (q: { from?: string; to?: string; resourceId?: string; projectId?: string }) => {
  const p = new URLSearchParams();
  if (q.from) p.set('from', q.from);
  if (q.to) p.set('to', q.to);
  if (q.resourceId) p.set('resourceId', q.resourceId);
  if (q.projectId) p.set('projectId', q.projectId);
  const s = p.toString();
  return apiGet<TimeEntry[]>(`/api/time-entries${s ? `?${s}` : ''}`);
};
export const createTimeEntry = (b: TimeEntryRequestBody) => apiPost<TimeEntry>('/api/time-entries', b);
export const updateTimeEntry = (id: string, b: TimeEntryRequestBody) =>
  apiPut<TimeEntry>(`/api/time-entries/${id}`, b);
export const deleteTimeEntry = (id: string) => apiDelete(`/api/time-entries/${id}`);

export const getTravelRecords = (q: { from?: string; to?: string; resourceId?: string; projectId?: string }) => {
  const p = new URLSearchParams();
  if (q.from) p.set('from', q.from);
  if (q.to) p.set('to', q.to);
  if (q.resourceId) p.set('resourceId', q.resourceId);
  if (q.projectId) p.set('projectId', q.projectId);
  const s = p.toString();
  return apiGet<TravelRecord[]>(`/api/travel-records${s ? `?${s}` : ''}`);
};
export const createTravelRecord = (b: TravelRecordRequestBody) =>
  apiPost<TravelRecord>('/api/travel-records', b);
export const updateTravelRecord = (id: string, b: TravelRecordRequestBody) =>
  apiPut<TravelRecord>(`/api/travel-records/${id}`, b);
export const deleteTravelRecord = (id: string) => apiDelete(`/api/travel-records/${id}`);

export const getExpenses = (q: { from?: string; to?: string; resourceId?: string; projectId?: string }) => {
  const p = new URLSearchParams();
  if (q.from) p.set('from', q.from);
  if (q.to) p.set('to', q.to);
  if (q.resourceId) p.set('resourceId', q.resourceId);
  if (q.projectId) p.set('projectId', q.projectId);
  const s = p.toString();
  return apiGet<Expense[]>(`/api/expenses${s ? `?${s}` : ''}`);
};
export const createExpense = (b: ExpenseRequestBody) => apiPost<Expense>('/api/expenses', b);
export const updateExpense = (id: string, b: ExpenseRequestBody) =>
  apiPut<Expense>(`/api/expenses/${id}`, b);
export const deleteExpense = (id: string) => apiDelete(`/api/expenses/${id}`);

export const getAllowances = (q: { from?: string; to?: string; resourceId?: string }) => {
  const p = new URLSearchParams();
  if (q.from) p.set('from', q.from);
  if (q.to) p.set('to', q.to);
  if (q.resourceId) p.set('resourceId', q.resourceId);
  const s = p.toString();
  return apiGet<AllowanceClaim[]>(`/api/allowances${s ? `?${s}` : ''}`);
};
export const createAllowance = (b: AllowanceRequestBody) => apiPost<AllowanceClaim>('/api/allowances', b);
export const deleteAllowance = (id: string) => apiDelete(`/api/allowances/${id}`);

export const getMobilisations = (q: { projectId?: string; resourceId?: string }) => {
  const p = new URLSearchParams();
  if (q.projectId) p.set('projectId', q.projectId);
  if (q.resourceId) p.set('resourceId', q.resourceId);
  const s = p.toString();
  return apiGet<MobilisationPeriod[]>(`/api/mobilisations${s ? `?${s}` : ''}`);
};
export const suggestMobilisations = (resourceId: string, from: string, to: string) =>
  apiGet<MobilisationSuggestion[]>(
    `/api/mobilisations/suggest?resourceId=${resourceId}&from=${from}&to=${to}`);
export const confirmMobilisation = (b: {
  projectId: string; resourceId: string; startDate: string; endDate: string;
}) => apiPost<MobilisationPeriod>('/api/mobilisations', b);
export const splitMobilisation = (id: string, fromDate: string) =>
  apiPost<{ first: MobilisationPeriod; second: MobilisationPeriod }>(
    `/api/mobilisations/${id}/split`, { fromDate });
export const mergeMobilisation = (id: string, otherId: string) =>
  apiPost<MobilisationPeriod>(`/api/mobilisations/${id}/merge`, { otherId });
export const deleteMobilisation = (id: string) => apiDelete(`/api/mobilisations/${id}`);

export const getTimesheets = (q: { resourceId?: string; status?: TimesheetStatus } = {}) => {
  const p = new URLSearchParams();
  if (q.resourceId) p.set('resourceId', q.resourceId);
  if (q.status) p.set('status', q.status);
  const s = p.toString();
  return apiGet<Timesheet[]>(`/api/timesheets${s ? `?${s}` : ''}`);
};
export const getTimesheet = (id: string) => apiGet<TimesheetDetail>(`/api/timesheets/${id}`);
export const submitTimesheet = (year: number, isoWeek: number, resourceId?: string) =>
  apiPost<TimesheetDetail>('/api/timesheets/submit', { year, isoWeek, resourceId });
export const timesheetAction = (id: string, action: 'approve' | 'return' | 'reject', note?: string) =>
  apiPost<Timesheet>(`/api/timesheets/${id}/${action}`, { note });

export const getDocuments = (ownerType: DocumentOwnerType, ownerId: string) =>
  apiGet<DocumentAttachment[]>(`/api/documents?ownerType=${ownerType}&ownerId=${ownerId}`);
export const uploadDocument = (ownerType: DocumentOwnerType, ownerId: string, file: File, label?: string) =>
  apiUpload<DocumentAttachment>(`/api/documents?ownerType=${ownerType}&ownerId=${ownerId}`, file, label);
export const deleteDocument = (id: string) => apiDelete(`/api/documents/${id}`);
/** Download URL — the dev-user header is needed, so fetch it through
 * documentBlob() rather than linking directly. */
export const documentBlob = async (id: string) => {
  const res = await fetch(`/api/documents/${id}/content`, { headers: { ...devHeaders() } });
  if (!res.ok) throw new Error(await res.text());
  return res.blob();
};

// ── Billing ─────────────────────────────────────────────────────────────────

export const getInvoices = (q: { projectId?: string; year?: number; month?: number; status?: string } = {}) => {
  const p = new URLSearchParams();
  if (q.projectId) p.set('projectId', q.projectId);
  if (q.year) p.set('year', String(q.year));
  if (q.month) p.set('month', String(q.month));
  if (q.status) p.set('status', q.status);
  const s = p.toString();
  return apiGet<InvoiceSpecification[]>(`/api/invoices${s ? `?${s}` : ''}`);
};
export const getInvoice = (id: string) => apiGet<InvoiceDetail>(`/api/invoices/${id}`);
export const generateInvoice = (projectId: string, year: number, month: number) =>
  apiPost<InvoiceSpecification>('/api/invoices/generate', { projectId, year, month });
export const invoiceAction = (id: string, action: 'review' | 'finalize' | 'issue' | 'pay') =>
  apiPost<InvoiceSpecification>(`/api/invoices/${id}/${action}`);
export const deleteInvoice = (id: string) => apiDelete(`/api/invoices/${id}`);

export const getStatements = (q: { resourceId?: string; year?: number; month?: number; status?: string } = {}) => {
  const p = new URLSearchParams();
  if (q.resourceId) p.set('resourceId', q.resourceId);
  if (q.year) p.set('year', String(q.year));
  if (q.month) p.set('month', String(q.month));
  if (q.status) p.set('status', q.status);
  const s = p.toString();
  return apiGet<SubcontractorStatement[]>(`/api/statements${s ? `?${s}` : ''}`);
};
export const getStatement = (id: string) => apiGet<StatementDetail>(`/api/statements/${id}`);
export const generateStatement = (resourceId: string, year: number, month: number) =>
  apiPost<SubcontractorStatement>('/api/statements/generate', { resourceId, year, month });
export const statementAction = (id: string, action: 'finalize' | 'pay') =>
  apiPost<SubcontractorStatement>(`/api/statements/${id}/${action}`);
export const deleteStatement = (id: string) => apiDelete(`/api/statements/${id}`);

export const getSupplierInvoices = (q: { resourceId?: string; status?: string } = {}) => {
  const p = new URLSearchParams();
  if (q.resourceId) p.set('resourceId', q.resourceId);
  if (q.status) p.set('status', q.status);
  const s = p.toString();
  return apiGet<SupplierInvoice[]>(`/api/supplier-invoices${s ? `?${s}` : ''}`);
};
export const getSupplierInvoice = (id: string) =>
  apiGet<SupplierInvoiceDetail>(`/api/supplier-invoices/${id}`);
export const registerSupplierInvoice = (body: {
  resourceId: string; number: string; invoiceDate: string; dueDate?: string | null;
  currency: string; totalAmount: number;
  lines?: { kind: string; description: string; amount: number }[];
}) => apiPost<SupplierInvoice>('/api/supplier-invoices', body);
export const matchSupplierInvoice = (id: string, statementId: string) =>
  apiPost<SupplierInvoiceDetail>(`/api/supplier-invoices/${id}/match`, { statementId });
export const supplierInvoiceAction = (id: string, action: 'approve' | 'pay') =>
  apiPost<SupplierInvoice>(`/api/supplier-invoices/${id}/${action}`);
export const deleteSupplierInvoice = (id: string) => apiDelete(`/api/supplier-invoices/${id}`);

export const getMargins = (year: number, month: number) =>
  apiGet<MarginRow[]>(`/api/reports/margins?year=${year}&month=${month}`);
