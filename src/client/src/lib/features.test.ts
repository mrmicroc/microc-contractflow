import { afterEach, describe, expect, it, vi } from 'vitest';

const apiGet = vi.fn();
vi.mock('./api', () => ({ apiGet: (path: string) => apiGet(path) }));

const { DEFAULT_FEATURES, loadFeatures, resetFeaturesCache, showPad } = await import('./features');

afterEach(() => {
  resetFeaturesCache();
  apiGet.mockReset();
});

describe('showPad', () => {
  it('hides PAD when the feature is off and the record does not use it', () => {
    expect(showPad({ pad: false })).toBe(false);
  });
  it('shows PAD for a record already using it, even with the feature off', () => {
    expect(showPad({ pad: false }, true)).toBe(true);
  });
  it('shows PAD everywhere when the feature is on', () => {
    expect(showPad({ pad: true })).toBe(true);
  });
});

describe('loadFeatures', () => {
  it('reads the server switches once', async () => {
    apiGet.mockResolvedValue({ pad: true });
    expect(await loadFeatures()).toEqual({ pad: true });
    await loadFeatures();
    expect(apiGet).toHaveBeenCalledTimes(1);
    expect(apiGet).toHaveBeenCalledWith('/api/meta/features');
  });
  it('falls back to everything off when the server cannot answer', async () => {
    apiGet.mockRejectedValue(new Error('offline'));
    expect(await loadFeatures()).toEqual(DEFAULT_FEATURES);
  });
});
