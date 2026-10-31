import { FormEvent, useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AdminCustomer, AdminFd, api, DepositsReport, JobRunResult, Paged } from '../api';
import { errorMessage, formatDate, formatDateTime, money } from '../format';
import { Pager } from '../components/Pager';

type Tab = 'report' | 'customers' | 'fds' | 'job';

function Report() {
  const q = useQuery({ queryKey: ['admin', 'report'], queryFn: () => api<DepositsReport>('/api/admin/reports/deposits') });
  if (q.isLoading) return <p className="muted">Loading...</p>;
  if (q.error) return <div className="alert error">{errorMessage(q.error)}</div>;
  const r = q.data!;
  const stats: [string, string][] = [
    ['Total deposits', money(r.totalDeposits)],
    ['Savings balances', money(r.savingsBalance)],
    ['Active FD principal', money(r.activeFdPrincipal)],
    ['Active fixed deposits', String(r.activeFdCount)],
    ['Customers', String(r.customerCount)],
    ['Accounts', String(r.accountCount)],
    ['Accrued interest on active FDs', money(r.accruedInterestOnActiveFds)],
  ];
  return (
    <div>
      <p className="muted small">As of {formatDate(r.asOf)}. Total deposits = savings balances + active FD principal.</p>
      <div className="stats">
        {stats.map(([label, value]) => (
          <div className="card stat" key={label}>
            <div className="muted small">{label}</div>
            <div className="stat-value">{value}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Customers() {
  const [page, setPage] = useState(1);
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  const q = useQuery({
    queryKey: ['admin', 'customers', search, page],
    queryFn: () => api<Paged<AdminCustomer>>(
      `/api/admin/customers?${new URLSearchParams({ search, page: String(page), pageSize: '15' })}`),
    placeholderData: keepPreviousData,
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    setPage(1);
    setSearch(text.trim());
  }

  return (
    <div>
      <form className="inline-form" onSubmit={submit}>
        <input placeholder="Search by name or email" value={text} onChange={(e) => setText(e.target.value)} />
        <button className="btn">Search</button>
      </form>
      {q.error && <div className="alert error">{errorMessage(q.error)}</div>}
      {q.data && (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Name</th><th>Email</th><th>Joined</th><th className="num">Savings</th>
                  <th className="num">Active FDs</th><th className="num">FD principal</th><th className="num">Total deposits</th>
                </tr>
              </thead>
              <tbody>
                {q.data.items.map((c) => (
                  <tr key={c.id}>
                    <td>{c.fullName}</td><td>{c.email}</td><td>{formatDateTime(c.createdAt)}</td>
                    <td className="num">{money(c.savingsBalance)}</td>
                    <td className="num">{c.activeFdCount}</td>
                    <td className="num">{money(c.activeFdPrincipal)}</td>
                    <td className="num"><strong>{money(c.totalDeposits)}</strong></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager page={q.data.page} pageSize={q.data.pageSize} total={q.data.totalCount} onChange={setPage} />
        </>
      )}
    </div>
  );
}

function FixedDepositsTab() {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('All');
  const q = useQuery({
    queryKey: ['admin', 'fds', status, page],
    queryFn: () => api<Paged<AdminFd>>(
      `/api/admin/fixed-deposits?${new URLSearchParams({ status, page: String(page), pageSize: '15' })}`),
    placeholderData: keepPreviousData,
  });

  return (
    <div>
      <div className="inline-form">
        <label className="inline">Status
          <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
            {['All', 'Active', 'Matured', 'Closed'].map((s) => <option key={s}>{s}</option>)}
          </select>
        </label>
      </div>
      {q.error && <div className="alert error">{errorMessage(q.error)}</div>}
      {q.data && (
        <>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Customer</th><th>Account</th><th className="num">Principal</th><th className="num">Rate</th>
                  <th>Opened</th><th>Matures</th><th className="num">Days left</th>
                  <th className="num">Maturity value</th><th>Status</th>
                </tr>
              </thead>
              <tbody>
                {q.data.items.map((f) => (
                  <tr key={f.id}>
                    <td>{f.customerName}</td><td>{f.accountNumber}</td>
                    <td className="num">{money(f.principal)}</td>
                    <td className="num">{f.annualRatePercent.toFixed(2)}%</td>
                    <td>{formatDate(f.startDate)}</td><td>{formatDate(f.maturityDate)}</td>
                    <td className="num">{f.daysToMaturity ?? '-'}</td>
                    <td className="num">{money(f.maturityAmount)}</td>
                    <td><span className={`badge ${f.status.toLowerCase()}`}>{f.status}</span></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <Pager page={q.data.page} pageSize={q.data.pageSize} total={q.data.totalCount} onChange={setPage} />
        </>
      )}
    </div>
  );
}

function JobTab() {
  const qc = useQueryClient();
  const [date, setDate] = useState('');
  const m = useMutation({
    mutationFn: () => api<JobRunResult>(`/api/admin/jobs/run${date ? `?date=${date}` : ''}`, { method: 'POST' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin'] }),
  });

  return (
    <div className="card">
      <h3>Run interest job</h3>
      <p className="muted small">
        Accrues daily interest for every active fixed deposit and credits matured ones to savings.
        It is safe to run repeatedly: a second run for the same date creates nothing new.
      </p>
      <div className="inline-form">
        <label className="inline">Date (optional)
          <input type="date" value={date} onChange={(e) => setDate(e.target.value)} />
        </label>
        <button className="btn" disabled={m.isPending} onClick={() => m.mutate()}>
          {m.isPending ? 'Running...' : 'Run job'}
        </button>
      </div>
      {m.error && <div className="alert error">{errorMessage(m.error)}</div>}
      {m.data && (
        <dl className="details">
          <dt>Run date</dt><dd>{formatDate(m.data.date)}</dd>
          <dt>FDs processed</dt><dd>{m.data.fdsProcessed}</dd>
          <dt>Accruals created</dt><dd>{m.data.accrualsCreated}</dd>
          <dt>Interest accrued</dt><dd>{money(m.data.interestAccrued)}</dd>
          <dt>FDs matured</dt><dd>{m.data.fdsMatured}</dd>
          <dt>Paid out at maturity</dt><dd>{money(m.data.maturityPaidOut)}</dd>
          <dt>Failures</dt><dd>{m.data.failures}</dd>
        </dl>
      )}
    </div>
  );
}

export function Admin() {
  const [tab, setTab] = useState<Tab>('report');
  const tabs: [Tab, string][] = [['report', 'Deposits report'], ['customers', 'Customers'], ['fds', 'Fixed deposits'], ['job', 'Interest job']];
  return (
    <div>
      <div className="tabs">
        {tabs.map(([id, label]) => (
          <button key={id} className={tab === id ? 'tab active' : 'tab'} onClick={() => setTab(id)}>{label}</button>
        ))}
      </div>
      {tab === 'report' && <Report />}
      {tab === 'customers' && <Customers />}
      {tab === 'fds' && <FixedDepositsTab />}
      {tab === 'job' && <JobTab />}
    </div>
  );
}