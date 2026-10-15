import type { en } from './en';

type DeepStringify<T> = {
  [K in keyof T]: T[K] extends string ? string : DeepStringify<T[K]>;
};

export const sv: DeepStringify<typeof en> = {
  appName: 'MICROC ContractFlow',
  appTagline: 'Kontrakt, resurser, tid och ekonomi',
  nav: {
    dashboard: 'Översikt',
    plan: 'Planering',
    projects: 'Projekt och kontrakt',
    resources: 'Resurser',
    myWork: 'Mitt arbete',
    approvals: 'Godkännanden',
    monthEnd: 'Månadsbokslut',
    economy: 'Ekonomi',
    reports: 'Rapporter',
    settings: 'Inställningar',
  },
  devBar: {
    actingAs: 'Arbetar som',
    devMode: 'utvecklingsläge — ingen inloggning',
  },
  language: {
    label: 'Språk',
    en: 'English',
    sv: 'Svenska',
  },
  placeholder: {
    title: 'Under uppbyggnad',
    body: 'Den här delen är inte byggd ännu.',
  },
  dashboard: {
    welcome: 'Välkommen',
    signedInAs: 'Du arbetar som {{name}} ({{role}}).',
  },
};
