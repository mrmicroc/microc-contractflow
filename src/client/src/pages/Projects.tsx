import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getCustomer, getCustomers } from '@/lib/api';
import type { CustomerDetail, CustomerSummary } from '@/lib/types';
import { canSeeCommercial, useMe } from '@/lib/useMe';
import { CustomerSection } from '@/components/commercial/CustomerSection';
import { ContractSection } from '@/components/commercial/ContractSection';
import { ContractDetail } from '@/components/commercial/ContractDetail';
import { ProjectDetail } from '@/components/commercial/ProjectDetail';

export function Projects() {
  const { t } = useTranslation();
  const me = useMe();
  const [customerId, setCustomerId] = useState<string | null>(null);
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [contractId, setContractId] = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(null);
  const [contractKey, setContractKey] = useState(0);
  // The whole organisation register — party and end-client pickers draw
  // from the same list.
  const [organisations, setOrganisations] = useState<CustomerSummary[]>([]);

  const reloadOrganisations = useCallback(
    () => getCustomers().then(setOrganisations).catch(() => setOrganisations([])),
    [],
  );
  useEffect(() => { void reloadOrganisations(); }, [reloadOrganisations]);

  const reloadCustomer = useCallback(() => {
    if (!customerId) return;
    getCustomer(customerId).then(setCustomer).catch(() => setCustomer(null));
  }, [customerId]);
  useEffect(() => { void reloadCustomer(); }, [reloadCustomer]);

  const selectCustomer = (id: string) => {
    if (id === customerId) {
      setCustomerId(null);
      setCustomer(null);
      setContractId(null);
      setProjectId(null);
      return;
    }
    setCustomer(null);
    setCustomerId(id);
    setContractId(null);
    setProjectId(null);
  };

  const selectContract = (id: string) => {
    setContractId((prev) => (prev === id ? null : id));
    setProjectId(null);
  };

  const selectProject = (id: string) => {
    setProjectId((prev) => (prev === id ? null : id));
  };

  if (me && !canSeeCommercial(me)) {
    return (
      <div className="mx-auto max-w-6xl px-4 py-8">
        <p className="text-sm text-slate-500">{t('common.noAccess')}</p>
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-6xl space-y-4 px-4 py-6">
      <CustomerSection
        selectedId={customerId}
        onSelect={selectCustomer}
        onChanged={() => void reloadOrganisations()}
        onDeleted={(id) => {
          if (id === customerId) {
            setCustomerId(null);
            setCustomer(null);
            setContractId(null);
            setProjectId(null);
          }
        }}
      />

      {customer && (
        <ContractSection
          customer={customer}
          organisations={organisations}
          selectedId={contractId}
          onSelect={selectContract}
          onChanged={() => { reloadCustomer(); void reloadOrganisations(); }}
          onDeleted={(id) => {
            if (id === contractId) {
              setContractId(null);
              setProjectId(null);
            }
          }}
        />
      )}

      {contractId && (
        <ContractDetail
          key={`${contractId}:${contractKey}`}
          contractId={contractId}
          selectedProjectId={projectId}
          onSelectProject={selectProject}
          onChanged={reloadCustomer}
        />
      )}

      {projectId && (
        <ProjectDetail
          key={projectId}
          projectId={projectId}
          onChanged={() => setContractKey((k) => k + 1)}
          onDeleted={() => {
            setProjectId(null);
            setContractKey((k) => k + 1); // remount → contract refetches its project list
          }}
        />
      )}

    </div>
  );
}
