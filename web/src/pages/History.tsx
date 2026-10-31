import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Account, api, Paged, Txn } from '../api';
import { errorMessage } from '../format';
import { Pager } from '../components/Pager';
import { TxnTable } from '../components/TxnTable';

const PAGE_SIZE = 15;

export function History() {
  const [page, setPage] = useState(1);
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: () => api<Account[]>('/api/accounts') });
  const id = accounts.data?.[0]?.id;
  const q = useQuery({
    queryKey: ['transactions', id, page],
    queryFn: () => api<Paged<Txn>>(`/api/accounts/${id}/transactions?page=${page}&pageSize=${PAGE_SIZE}`),
    enabled: !!id,
    placeholderData: keepPreviousData,
  });

  if (accounts.error || q.error) return <div className="alert error">{errorMessage(accounts.error ?? q.error)}</div>;

  return (
    <div className="card">
      <h2>Transaction history</h2>
      {q.isLoading || !q.data ? (
        <p className="muted">Loading...</p>
      ) : (
        <>
          <TxnTable items={q.data.items} />
          <Pager page={q.data.page} pageSize={q.data.pageSize} total={q.data.totalCount} onChange={setPage} />
        </>
      )}
    </div>
  );
}