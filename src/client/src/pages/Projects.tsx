import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { createEndClient, getCustomer, getEndClients } from '@/lib/api';
import type { CustomerDetail, EndClient } from '@/lib/types';
import { canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import { CustomerSection } from '@/components/commercial/CustomerSection';
import { ContractSection } from '@/components/commercial/ContractSection';
import { ContractDetail } from '@/components/commercial/ContractDetail';
import { ProjectDetail } from '@/components/commercial/ProjectDetail';
import { btnCls, Field, inputCls, Panel, tdCls } from '@/components/forms';

export function Projects() {
  const { t } = useTranslation();
  const me = useMe();
  const [customerId, setCustomerId] = useState<string | null>(null);
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [contractId, setContractId] = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(null);
  const [endClients, setEndClients] = useState<EndClient[]>([]);
  const [newEndClient, setNewEndClient] = useState('');

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
    if (id !== customerId) setCustomer(null);
    setCustomerId(id);
    setContractId(null);
    setProjectId(null);
  };

  const selectContract = (id: string) => {
    setContractId(id);
    setProjectId(null);
  };

  const saveEndClient = async () => {
    await createEndClient({ name: newEndClient.trim(), notes: null });
    setNewEndClient('');
    await reloadEndClients();
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
      <CustomerSection selectedId={customerId} onSelect={selectCustomer} />

      {customer && (
        <ContractSection
          customer={customer}
          endClients={endClients}
          selectedId={contractId}
          onSelect={selectContract}
          onChanged={reloadCustomer}
        />
      )}

      {contractId && (
        <ContractDetail
          key={contractId}
          contractId={contractId}
          selectedProjectId={projectId}
          onSelectProject={setProjectId}
        />
      )}

      {projectId && <ProjectDetail key={projectId} projectId={projectId} />}

      {isAdmin(me) && (
        <Panel title={t('endClients.title')}>
          <table className="mb-3 w-full border-collapse">
            <tbody>
              {endClients.map((e) => (
                <tr key={e.id} className="border-b border-slate-100">
                  <td className={tdCls}>{e.name}</td>
                </tr>
              ))}
              {endClients.length === 0 && (
                <tr><td className={tdCls}>{t('common.none')}</td></tr>
              )}
            </tbody>
          </table>
          <div className="flex items-end gap-2">
            <Field label={t('common.name')}>
              <input className={inputCls} value={newEndClient}
                onChange={(e) => setNewEndClient(e.target.value)} />
            </Field>
            <button className={btnCls} disabled={!newEndClient.trim()}
              onClick={() => void saveEndClient()}>
              {t('endClients.newEndClient')}
            </button>
          </div>
        </Panel>
      )}
    </div>
  );
}
