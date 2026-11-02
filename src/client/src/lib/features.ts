import { useEffect, useState } from 'react';
import { apiGet } from './api';

/** Product features switched on for this installation (GET /api/meta/features). */
export type Features = { pad: boolean };

/** Until the server answers — and if it can't — features stay off. */
export const DEFAULT_FEATURES: Features = { pad: false };

let cached: Promise<Features> | null = null;

/** Loads the feature switches once per page load. */
export function loadFeatures(): Promise<Features> {
  cached ??= apiGet<Features>('/api/meta/features').catch(() => DEFAULT_FEATURES);
  return cached;
}

/** Test seam: forget the cached answer. */
export function resetFeaturesCache() {
  cached = null;
}

export function useFeatures(): Features {
  const [features, setFeatures] = useState<Features>(DEFAULT_FEATURES);
  useEffect(() => {
    let alive = true;
    void loadFeatures().then((f) => { if (alive) setFeatures(f); });
    return () => { alive = false; };
  }, []);
  return features;
}

/**
 * Whether PAD fields show: the PAD feature is on, or the record in front of
 * the user already uses PAD — existing PAD data stays visible (and can be
 * switched off) even when new contracts no longer offer it.
 */
export function showPad(features: Features, inUse = false): boolean {
  return features.pad || inUse;
}
