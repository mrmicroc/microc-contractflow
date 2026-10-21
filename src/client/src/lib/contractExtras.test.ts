import { describe, expect, it } from 'vitest';
import {
  billedProjectName, canChangeCharge, chargeBody, chargeToForm, emptyCharge, representativeNames,
  representativesBody, repToRow, visitsSummary,
} from './contractExtras';
import type { ProjectSummary, ScheduledCharge } from './types';

const charge: ScheduledCharge = {
  id: 'c1', contractId: 'k', projectId: null, projectName: null, billedProjectId: 'p2',
  date: '2027-03-15', amount: 50000, description: 'Set-up fee', invoiced: false,
};
const projects: ProjectSummary[] = [
  { id: 'p1', contractId: 'k', name: 'Alpha', code: null, isActive: true },
  { id: 'p2', contractId: 'k', name: 'Beta', code: null, isActive: true },
];

describe('scheduled charges', () => {
  it('maps a valid form (comma decimals, trimmed text, empty project = default)', () => {
    expect(chargeBody({ date: '2027-03-15', amount: '12500,50', description: '  Milestone 1 ', projectId: '' }))
      .toEqual({ date: '2027-03-15', amount: 12500.5, description: 'Milestone 1', projectId: null });
  });

  it('refuses incomplete or non-positive amounts', () => {
    expect(chargeBody(emptyCharge)).toBeNull();
    expect(chargeBody({ date: '2027-01-01', amount: '0', description: 'x', projectId: '' })).toBeNull();
    expect(chargeBody({ date: '2027-01-01', amount: 'abc', description: 'x', projectId: '' })).toBeNull();
    expect(chargeBody({ date: '2027-01-01', amount: '10', description: '  ', projectId: '' })).toBeNull();
  });

  it('round-trips a stored charge into the form', () => {
    expect(chargeToForm({ ...charge, projectId: 'p1' }))
      .toEqual({ date: '2027-03-15', amount: '50000', description: 'Set-up fee', projectId: 'p1' });
  });

  it('names the billing project — chosen, else the default', () => {
    expect(billedProjectName(charge, projects)).toBe('Beta');
    expect(billedProjectName({ ...charge, projectName: 'Alpha' }, projects)).toBe('Alpha');
    expect(billedProjectName({ ...charge, billedProjectId: null }, projects)).toBeNull();
  });

  it('an invoiced charge is frozen', () => {
    expect(canChangeCharge(charge)).toBe(true);
    expect(canChangeCharge({ ...charge, invoiced: true })).toBe(false);
  });
});

describe('representatives', () => {
  it('drops nameless rows and nulls blank fields', () => {
    expect(representativesBody([
      { name: ' Alex Example ', email: 'alex@example.org', phone: '', authority: ' bookings ' },
      { name: '  ', email: 'x@example.org', phone: '', authority: '' },
    ])).toEqual([{ name: 'Alex Example', email: 'alex@example.org', phone: null, authority: 'bookings' }]);
  });

  it('round-trips and lists names for the picker', () => {
    const rep = { name: 'Sam Example', email: null, phone: '+46 0', authority: null };
    expect(repToRow(rep)).toEqual({ name: 'Sam Example', email: '', phone: '+46 0', authority: '' });
    expect(representativeNames([rep])).toEqual(['Sam Example']);
    expect(representativeNames(undefined)).toEqual([]);
  });
});

describe('visitsSummary', () => {
  it('reads used / included / remaining, never negative', () => {
    expect(visitsSummary({ used: 3, included: 20, remaining: 17, visits: [] }))
      .toEqual({ key: 'visits.used', params: { used: 3, included: 20, remaining: 17 } });
    expect(visitsSummary({ used: 22, included: 20, remaining: null, visits: [] }).params.remaining).toBe(0);
  });

  it('without a limit only counts', () => {
    expect(visitsSummary({ used: 4, included: null, remaining: null, visits: [] }))
      .toEqual({ key: 'visits.usedUnlimited', params: { used: 4 } });
  });
});
