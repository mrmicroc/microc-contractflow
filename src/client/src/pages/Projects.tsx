import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  createEndClient, deleteEndClient, errorMessage, getCustomer, getEndClients, updateEndClient,
} from '@/lib/api';
import type { CustomerDetail, EndClient } from '@/lib/types';
import { canSeeCommercial, isAdmin, useMe } from '@/lib/useMe';
import { CustomerSection } from '@/components/commercial/CustomerSection';
import { ContractSection } from '@/components/commercial/ContractSection';
import { ContractDetail } from '@/components/commercial/ContractDetail';
import { ProjectDetail } from '@/components/commercial/ProjectDetail';
import {
  btnCls, btnDangerCls, btnSecondaryCls, Field, inputCls, Panel, tdCls,
} from '@/components/forms';

export function Projects() {
  const { t } = useTranslation();
  const me = useMe();
  const [customerId, setCustomerId] = useState<string | null>(null);
  const [customer, setCustomer] = useState<CustomerDetail | null>(null);
  const [contractId, setContractId] = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(null);
  const [contractKey, setContractKey] = useState(0);
  const [endClients, setEndClients] = useState<EndClient[]>([]);
  const [newEndClient, setNewEndClient] = useState('');
  const [editEndClientId, setEditEndClientId] = useState<string | null>(null);
  const [editEndClientName, setEditEndClientName] = useState('');
  const [endClientError, setEndClientError] = useState<string | null>(null);

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
    setEndClientError(null);
    try {
      await createEndClient({ name: newEndClient.trim(), notes: null });
      setNewEndClient('');
      await reloadEndClients();
    } catch (e) { setEndClientError(errorMessage(e)); }
  };

  const saveEndClientEdit = async () => {
    if (!editEndClientId) return;
    setEndClientError(null);
    try {
      await updateEndClient(editEndClientId, { name: editEndClientName.trim(), notes: null });
      setEditEndClientId(null);
      await reloadEndClients();
    } catch (e) { setEndClientError(errorMessage(e)); }
  };

  const removeEndClient = async (id: string) => {
    if (!window.confirm(t('common.confirmDelete'))) return;
    setEndClientError(null);
    try { await deleteEndClient(id); await reloadEndClients(); }
    catch (e) { setEndClientError(errorMessage(e)); }
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
          onSelectProject={setProjectId}
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

      {isAdmin(me) && (
        <Panel title={t('endClients.title')}>
          {endClientError && (
            <p className="mb-2 rounded bg-red-50 px-3 py-1.5 text-sm text-red-700">{endClientError}</p>
          )}
          <table className="mb-3 w-full border-collapse">
            <tbody>
              {endClients.map((e) => (
                <tr key={e.id} className="border-b border-slate-100">
                  <td className={tdCls}>
                    {editEndClientId === e.id ? (
                      <input className={inputCls} value={editEndClientName}
                        onChange={(ev) => setEditEndClientName(ev.target.value)} />
                    ) : e.name}
                  </td>
                  <td className={tdCls}>
                    <span className="flex gap-1">
                      {editEndClientId === e.id ? (
                        <>
                          <button className={btnCls} disabled={!editEndClientName.trim()}
                            onClick={() => void saveEndClientEdit()}>
                            {t('common.save')}
                          </button>
                          <button className={btnSecondaryCls} onClick={() => setEditEndClientId(null)}>
                            {t('common.cancel')}
                          </button>
                        </>
                      ) : (
                        <>
                          <button className={btnSecondaryCls}
                            onClick={() => { setEditEndClientId(e.id); setEditEndClientName(e.name); }}>
                            {t('common.edit')}
                          </button>
                          <button className={btnDangerCls} onClick={() => void removeEndClient(e.id)}>
                            {t('common.delete')}
                          </button>
                        </>
                      )}
                    </span>
                  </td>
                </tr>
              ))}
              {endClients.length === 0 && (
                <tr><td className={tdCls} colSpan={2}>{t('common.none')}</td></tr>
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
