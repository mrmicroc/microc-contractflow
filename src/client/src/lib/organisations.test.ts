import { describe, expect, it } from 'vitest';
import {
  contractPartiesBody, endClientLabel, endClientOptions, endClientsOf,
} from './organisations';

const orgs = [
  { id: 'b', name: 'North Link' },
  { id: 'a', name: 'Example Org' },
  { id: 'c', name: 'Another Org' },
];

describe('endClientOptions', () => {
  it('offers every organisation except the party, sorted by name', () => {
    expect(endClientOptions(orgs, 'a').map((o) => o.name)).toEqual(['Another Org', 'North Link']);
  });

  it('keeps all organisations when no party is chosen yet', () => {
    expect(endClientOptions(orgs, '')).toHaveLength(3);
  });
});

describe('contractPartiesBody', () => {
  it('sends the end client and trimmed reference via an intermediary', () => {
    expect(contractPartiesBody({ customerId: 'a', endClientId: 'b', endClientReference: ' PO-1 ' }))
      .toEqual({ customerId: 'a', endClientId: 'b', endClientReference: 'PO-1' });
  });

  it('treats no end client as a direct contract and drops the reference', () => {
    expect(contractPartiesBody({ customerId: 'a', endClientId: '', endClientReference: 'PO-1' }))
      .toEqual({ customerId: 'a', endClientId: null, endClientReference: null });
  });

  it('treats the party picked as its own end client as a direct contract', () => {
    expect(contractPartiesBody({ customerId: 'a', endClientId: 'a', endClientReference: 'X' }))
      .toEqual({ customerId: 'a', endClientId: null, endClientReference: null });
  });

  it('sends an empty reference as null', () => {
    expect(contractPartiesBody({ customerId: 'a', endClientId: 'b', endClientReference: '  ' })
      .endClientReference).toBeNull();
  });
});

describe('endClientLabel', () => {
  it('is null on a direct contract', () => {
    expect(endClientLabel(null, 'ignored')).toBeNull();
  });

  it('appends the reference when present', () => {
    expect(endClientLabel('North Link', 'PO-4711')).toBe('North Link (PO-4711)');
    expect(endClientLabel('North Link', null)).toBe('North Link');
  });
});

describe('endClientsOf', () => {
  it('lists distinct effective end clients by name', () => {
    expect(endClientsOf([
      { endClientId: 'b', endClientName: 'North Link' },
      { endClientId: 'a', endClientName: 'Example Org' },
      { endClientId: 'b', endClientName: 'North Link' },
    ])).toEqual([{ id: 'a', name: 'Example Org' }, { id: 'b', name: 'North Link' }]);
  });
});
