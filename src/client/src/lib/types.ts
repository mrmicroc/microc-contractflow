// Mirrors the API DTOs in ContractFlow.Api/Models/Dto (camelCase JSON).

export type ContractModel = 'padBased' | 'hourly';
export type ResourceType = 'employee' | 'subcontractor' | 'directlyContracted';
export type LeaveType = 'vacation' | 'leave' | 'unavailable';
export type PerDiemPolicy = 'none' | 'statutory' | 'custom';

export interface CustomerSummary {
  id: string;
  name: string;
  externalReference: string | null;
  isActive: boolean;
  contractCount: number;
}

export interface CustomerDetail {
  id: string;
  name: string;
  externalReference: string | null;
  notes: string | null;
  isActive: boolean;
  contracts: ContractSummary[];
}

export interface CustomerRequestBody {
  name: string;
  externalReference: string | null;
  notes: string | null;
  isActive: boolean;
}

export interface EndClient {
  id: string;
  name: string;
  notes: string | null;
}

export interface EndClientRequestBody {
  name: string;
  notes: string | null;
}

export interface ContractSummary {
  id: string;
  customerId: string;
  name: string;
  externalReference: string | null;
  model: ContractModel;
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
  model: ContractModel;
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
  model: ContractModel;
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
  padRate: number | null;
  hourlyRate: number | null;
  saturdayRate: number | null;
  sundayRate: number | null;
  holidayRate: number | null;
  overtimeAfterHours: number;
  overtimePercent: number;
  mobilisationFee: number | null;
  expenseMarkupPercent: number;
  padHoursPerDay: number;
  minPadPerBookedDay: number;
  carryOverMaxPercent: number;
  cancellationDeadlineBusinessDays: number;
  holidayCalendar: string;
  travelExpensePolicy: string | null;
  indexation: string | null;
  notes: string | null;
}

export interface ContractTermsRequestBody {
  effectiveFrom: string;
  currency: string;
  padRate: number | null;
  hourlyRate: number | null;
  saturdayRate: number | null;
  sundayRate: number | null;
  holidayRate: number | null;
  overtimeAfterHours: number;
  overtimePercent: number;
  mobilisationFee: number | null;
  expenseMarkupPercent: number;
  padHoursPerDay: number;
  minPadPerBookedDay: number;
  carryOverMaxPercent: number;
  cancellationDeadlineBusinessDays: number;
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
  locations: Location[];
  estimatedQuantities: EstimatedQuantity[];
}

export interface ProjectRequestBody {
  contractId: string;
  name: string;
  code: string | null;
  description: string | null;
  isActive: boolean;
}

export interface Location {
  id: string;
  projectId: string;
  name: string;
  address: string | null;
  notes: string | null;
}

export interface LocationRequestBody {
  projectId: string;
  name: string;
  address: string | null;
  notes: string | null;
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
