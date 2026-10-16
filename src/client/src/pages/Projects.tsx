import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getCustomer, getEndClients } from '@/lib/api';
import type { CustomerDetail, EndClient } from '@/lib/types';
import { canSeeCommercial, useMe } from '@/lib/useMe';
import { CustomerSection } from '@/components/commercial/CustomerSection';
import { ContractSection } from '@/components/commercial/ContractSection';
import { ContractDetail } from '@/components/commercial/ContractDetail';
import { ProjectDetail } from '@/components/commercial/ProjectDetail';
import { EndClientSection } from '@/components/commercial/EndClientSection';

export function Projects() {
  const { t } = useTranslation();
  const me = useMe();
  const [customerId, setCustomerId] = useState<string | null>(null);
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [contractId, setContractId] = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(null);
  const [contractKey, setContractKey] = useState(0);
  const [endClients, setEndClients] = useState<EndClient[]>([]);

  const reloadEndClients = useCallback(
    () => getEndClients().then(setEndClients).catch(() => setEndClients([])),
    [],
  );
  useEffect(() => { void reloadEndClients(); }, [reloadEndClients]);

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
          endClients={endClients}
          selectedId={contractId}
          onSelect={selectContract}
          onChanged={reloadCustomer}
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
          onDeleted={() => {
            setProjectId(null);
            setContractKey((k) => k + 1); // remount → contract refetches its project list
          }}
        />
      )}

      <EndClientSection onChanged={reloadEndClients} />
    </div>
  );
}
