import { DEV_USER_HEADER, getDevUserId } from './currentUser';
import type {
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

export const getEndClients = () => apiGet<EndClient[]>('/api/endclients');
export const createEndClient = (b: EndClientRequestBody) => apiPost<EndClient>('/api/endclients', b);

export const getContracts = (customerId?: string) =>
  apiGet<ContractSummary[]>(`/api/contracts${customerId ? `?customerId=${customerId}` : ''}`);
export const getContract = (id: string) => apiGet<ContractDetail>(`/api/contracts/${id}`);
export const createContract = (b: ContractRequestBody) => apiPost<ContractSummary>('/api/contracts', b);
export const updateContract = (id: string, b: ContractRequestBody) =>
  apiPut<ContractSummary>(`/api/contracts/${id}`, b);
export const getContractTerms = (contractId: string) =>
  apiGet<ContractTerms[]>(`/api/contracts/${contractId}/terms`);
export const getEffectiveTerms = (contractId: string, on?: string) =>
  apiGet<ContractTerms>(`/api/contracts/${contractId}/terms/effective${on ? `?on=${on}` : ''}`);
export const createContractTerms = (contractId: string, b: ContractTermsRequestBody) =>
  apiPost<ContractTerms>(`/api/contracts/${contractId}/terms`, b);

export const getProjects = (contractId?: string) =>
  apiGet<ProjectSummary[]>(`/api/projects${contractId ? `?contractId=${contractId}` : ''}`);
export const getProject = (id: string) => apiGet<ProjectDetail>(`/api/projects/${id}`);
export const createProject = (b: ProjectRequestBody) => apiPost<ProjectSummary>('/api/projects', b);
export const updateProject = (id: string, b: ProjectRequestBody) =>
  apiPut<ProjectSummary>(`/api/projects/${id}`, b);

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
export const getAgreements = (resourceId: string) =>
  apiGet<CompensationAgreement[]>(`/api/resources/${resourceId}/agreements`);
export const createAgreement = (resourceId: string, b: AgreementRequestBody) =>
  apiPost<CompensationAgreement>(`/api/resources/${resourceId}/agreements`, b);
export const createLeave = (resourceId: string, b: ResourceLeaveRequestBody) =>
  apiPost<ResourceLeave>(`/api/resources/${resourceId}/leave`, b);
export const deleteLeave = (resourceId: string, leaveId: string) =>
  apiDelete(`/api/resources/${resourceId}/leave/${leaveId}`);

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
