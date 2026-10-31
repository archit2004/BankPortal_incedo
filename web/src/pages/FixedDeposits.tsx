import { FormEvent, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Account, api, Fd, Rate } from '../api';
import { errorMessage, formatDate, isMoney, money, previewMaturityDate } from '../format';
import { shouldRotateKey, useIdempotencyKey } from '../idem';

export function FixedDeposits() {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const key = useIdempotencyKey();
  const [amount, setAmount] = useState('100000');
  const [tenure, setTenure] = useState('12');
  const [localError, setLocalError] = useState('');

  const rates = useQuery({ queryKey: ['rates'], queryFn: () => api<Rate[]>('/api/fixed-deposits/rates') });
  const accounts = useQuery({ queryKey: ['accounts'], queryFn: () => api<Account[]>('/api/accounts') });
  const fds = useQuery({ queryKey: ['fds'], queryFn: () => api<Fd[]>('/api/fixed-deposits') });
  const account = accounts.data?.[0];

  const months = Number(tenure);
  const amt = Number(amount);
  const validMonths = Number.isInteger(months) && months >= 1 && months <= 120;
  const validAmount = isMoney(amount) && amt >= 1000;
  const rate = validMonths
    ? rates.data?.find((r) => months >= r.minTenureMonths && months <= r.maxTenureMonths)
    : undefined;

  // Estimate only (quarterly compounding). The server calculates the real figures.
  const preview =
    rate && validAmount
      ? (() => {
          const maturity = amt * Math.pow(1 + rate.annualRatePercent / 100 / 4, (4 * months) / 12);
          return { maturity, interest: maturity - amt };
        })()
      : null;

  const open = useMutation({
    mutationFn: () =>
      api<Fd>(`/api/accounts/${account!.id}/fixed-deposits`, {
        method: 'POST',
        body: { amount: amt, tenureMonths: months },
        headers: { 'Idempotency-Key': key.get() },
      }),
    onSuccess: (fd) => {
      key.reset();
      qc.invalidateQueries({ queryKey: ['fds'] });
      qc.invalidateQueries({ queryKey: ['accounts'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      qc.invalidateQueries({ queryKey: ['transactions'] });
      navigate(`/fixed-deposits/${fd.id}`);
    },
    onError: (e) => {
      if (shouldRotateKey(e)) key.reset();
    },
  });

  function submit(e: FormEvent) {
    e.preventDefault();
    setLocalError('');
    if (!account) return;
    if (!validAmount) return setLocalError('Enter an amount of at least 1,000 with at most 2 decimals.');
    if (!validMonths) return setLocalError('Tenure must be a whole number of months between 1 and 120.');
    if (!rate) return setLocalError('No interest rate is available for this tenure. See the rate card.');
    if (amt > account.balance) return setLocalError('Insufficient savings balance for this deposit.');
    open.mutate();
  }

  return (
    <div>
      <div className="grid-2">
        <form className="card" onSubmit={submit}>
          <h3>Open a fixed deposit</h3>
          {account && <p className="muted small">Available savings balance: {money(account.balance)}</p>}
          {(localError || open.error) && <div className="alert error">{localError || errorMessage(open.error)}</div>}
          <label>Amount (INR)
            <input inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
          </label>
          <label>Tenure (months)
            <input inputMode="numeric" value={tenure} onChange={(e) => setTenure(e.target.value)} />
          </label>
          {preview && rate ? (
            <div className="preview">
              <div>Interest rate: <strong>{rate.annualRatePercent.toFixed(2)}% p.a.</strong></div>
              <div>Estimated maturity value: <strong>{money(preview.maturity)}</strong></div>
              <div>Estimated interest: {money(preview.interest)}</div>
              <div>Matures on about {previewMaturityDate(months)}</div>
              <div className="muted small">Estimate only. The final amounts are calculated by the bank when you confirm.</div>
            </div>
          ) : (
            <p className="muted small">Enter an amount and tenure to see an estimate.</p>
          )}
          <button className="btn" disabled={open.isPending || !account}>
            {open.isPending ? 'Opening...' : 'Open fixed deposit'}
          </button>
        </form>

        <div className="card">
          <h3>Rate card</h3>
          {rates.isLoading ? <p className="muted">Loading...</p> : (
            <table>
              <thead><tr><th>Tenure</th><th className="num">Rate p.a.</th></tr></thead>
              <tbody>
                {rates.data?.map((r) => (
                  <tr key={r.minTenureMonths}>
                    <td>{r.minTenureMonths} - {r.maxTenureMonths} months</td>
                    <td className="num">{r.annualRatePercent.toFixed(2)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          <p className="muted small">Closing before maturity earns a reduced rate with a 1% penalty.</p>
        </div>
      </div>

      <div className="card">
        <h3>Your fixed deposits</h3>
        {fds.isLoading ? <p className="muted">Loading...</p> : fds.data && fds.data.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Opened</th><th className="num">Amount</th><th className="num">Rate</th><th>Tenure</th>
                  <th>Matures</th><th className="num">Maturity value</th><th>Status</th><th />
                </tr>
              </thead>
              <tbody>
                {fds.data.map((f) => (
                  <tr key={f.id}>
                    <td>{formatDate(f.startDate)}</td>
                    <td className="num">{money(f.principal)}</td>
                    <td className="num">{f.annualRatePercent.toFixed(2)}%</td>
                    <td>{f.tenureMonths} mo</td>
                    <td>{formatDate(f.maturityDate)}</td>
                    <td className="num">{money(f.maturityAmount)}</td>
                    <td><span className={`badge ${f.status.toLowerCase()}`}>{f.status}</span></td>
                    <td><Link to={`/fixed-deposits/${f.id}`}>View</Link></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : <p className="muted">You have no fixed deposits yet.</p>}
      </div>
    </div>
  );
}