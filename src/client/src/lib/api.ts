import { DEV_USER_HEADER, getDevUserId } from './currentUser';
import type {
  AdditionalPadRequest,
  Booking,
  BookingRequestBody,
  CloseMonthResult,
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
  Location,
  LocationRequestBody,
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

export const apiPost = <T>(path: string, body: unknown) => apiSend<T>('POST', path, body);
export const apiPut = <T>(path: string, body: unknown) => apiSend<T>('PUT', path, body);
export const apiDelete = (path: string) => apiSend<void>('DELETE', path);

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
