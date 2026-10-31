import { FormEvent, useState } from 'react';
import { Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Account, api, DashboardData, Txn } from '../api';
import { errorMessage, isMoney, money } from '../format';
import { shouldRotateKey, useIdempotencyKey } from '../idem';
import { TxnTable } from '../components/TxnTable';

function MoneyForm({ accountId, kind }: { accountId: string; kind: 'deposit' | 'withdraw' }) {
  const qc = useQueryClient();
  const key = useIdempotencyKey();
  const [amount, setAmount] = useState('');
  const [reference, setReference] = useState('');
  const [localError, setLocalError] = useState('');
  const [notice, setNotice] = useState('');

  const m = useMutation({
    mutationFn: () =>
      api<Txn>(`/api/accounts/${accountId}/${kind}`, {
        method: 'POST',
        body: { amount: Number(amount), reference: reference.trim() || null },
        headers: { 'Idempotency-Key': key.get() },
      }),
    onSuccess: (t) => {
      key.reset();
      setAmount('');
      setReference('');
      setNotice(`Done. New balance: ${money(t.balanceAfter)}`);
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      qc.invalidateQueries({ queryKey: ['accounts'] });
      qc.invalidateQueries({ queryKey: ['transactions'] });
    },
    onError: (e) => {
      if (shouldRotateKey(e)) key.reset();
    },
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    setNotice('');
    setLocalError('');
    if (!isMoney(amount)) return setLocalError('Enter a positive amount with at most 2 decimals.');
    m.mutate();
  }

  const title = kind === 'deposit' ? 'Deposit' : 'Withdraw';
  return (
    <form className="card" onSubmit={submit}>
      <h3>{title}</h3>
      {notice && <div className="alert ok">{notice}</div>}
      {(localError || m.error) && <div className="alert error">{localError || errorMessage(m.error)}</div>}
      <label>Amount (INR)
        <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="0.00" />
      </label>
      <label>Reference (optional)
        <input value={reference} maxLength={200} onChange={(e) => setReference(e.target.value)} />
      </label>
      <button className="btn" disabled={m.isPending}>{m.isPending ? 'Working...' : title}</button>
    </form>
  );
}

export function Dashboard() {
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: () => api<Account[]>('/api/accounts') });
  const account = accounts.data?.[0];
  const dash = useQuery({
    queryKey: ['dashboard', account?.id],
    queryFn: () => api<DashboardData>(`/api/accounts/${account!.id}/dashboard`),
    enabled: !!account,
  });

  if (accounts.isLoading) return <p className="muted">Loading...</p>;
  if (accounts.error) return <div className="alert error">{errorMessage(accounts.error)}</div>;
  if (!account) return <div className="alert">No account found.</div>;

  const current = dash.data?.account ?? account;

  return (
    <div>
      <div className="card balance-card">
        <div>
          <div className="muted small">Savings account {current.accountNumber}</div>
          <div className="balance">{money(current.balance)}</div>
        </div>
        <span className="badge active">{current.status}</span>
      </div>

      <div className="grid-2">
        <MoneyForm accountId={account.id} kind="deposit" />
        <MoneyForm accountId={account.id} kind="withdraw" />
      </div>

      <div className="card">
        <div className="row-between">
          <h3>Recent transactions</h3>
          <Link to="/history">View all</Link>
        </div>
        {dash.isLoading ? <p className="muted">Loading...</p> : <TxnTable items={dash.data?.recentTransactions ?? []} />}
      </div>
    </div>
  );
}