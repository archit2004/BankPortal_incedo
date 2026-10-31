import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, CloseResult, FdDetailData } from '../api';
import { errorMessage, formatDate, money } from '../format';
import { shouldRotateKey, useIdempotencyKey } from '../idem';

export function FdDetail() {
  const { id } = useParams();
  const qc = useQueryClient();
  const key = useIdempotencyKey();
  const [confirming, setConfirming] = useState(false);
  const [result, setResult] = useState<CloseResult | null>(null);

  const q = useQuery({ queryKey: ['fd', id], queryFn: () => api<FdDetailData>(`/api/fixed-deposits/${id}`) });

  const close = useMutation({
    mutationFn: () => api<CloseResult>(`/api/fixed-deposits/${id}/close`, {
      method: 'POST',
      headers: { 'Idempotency-Key': key.get() },
    }),
    onSuccess: (r) => {
      key.reset();
      setConfirming(false);
      setResult(r);
      qc.invalidateQueries({ queryKey: ['fd', id] });
      qc.invalidateQueries({ queryKey: ['fds'] });
      qc.invalidateQueries({ queryKey: ['accounts'] });
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      qc.invalidateQueries({ queryKey: ['transactions'] });
    },
    onError: (e) => {
      if (shouldRotateKey(e)) key.reset();
    },
  });

  if (q.isLoading) return <p className="muted">Loading...</p>;
  if (q.error) return <div className="alert error">{errorMessage(q.error)}</div>;
  if (!q.data) return null;

  const { fd, closeQuote } = q.data;
  const reachedMaturity = closeQuote && closeQuote.asOf >= fd.maturityDate;

  return (
    <div>
      <p><Link to="/fixed-deposits">&larr; Back to fixed deposits</Link></p>

      {result && (
        <div className="alert ok">
          Fixed deposit closed. {money(result.payout)} was credited to your savings account.
          New balance: {money(result.balanceAfter)}.
        </div>
      )}

      <div className="card">
        <div className="row-between">
          <h2>Fixed deposit</h2>
          <span className={`badge ${fd.status.toLowerCase()}`}>{fd.status}</span>
        </div>
        <dl className="details">
          <dt>Amount</dt><dd>{money(fd.principal)}</dd>
          <dt>Interest rate</dt><dd>{fd.annualRatePercent.toFixed(2)}% p.a., compounded {fd.compoundingPerYear}x a year</dd>
          <dt>Tenure</dt><dd>{fd.tenureMonths} months</dd>
          <dt>Opened</dt><dd>{formatDate(fd.startDate)}</dd>
          <dt>Matures</dt><dd>{formatDate(fd.maturityDate)}</dd>
          <dt>Maturity value</dt><dd>{money(fd.maturityAmount)}</dd>
          {fd.closedOn && (<><dt>{fd.status === 'Matured' ? 'Matured on' : 'Closed on'}</dt><dd>{formatDate(fd.closedOn)}</dd></>)}
          {fd.penaltyApplied !== null && fd.penaltyApplied > 0 && (<><dt>Penalty applied</dt><dd>{money(fd.penaltyApplied)}</dd></>)}
        </dl>
      </div>

      {fd.status === 'Active' && closeQuote && (
        <div className="card">
          <h3>{reachedMaturity ? 'Close at maturity' : 'Close early'}</h3>
          <dl className="details">
            <dt>Days held</dt><dd>{closeQuote.daysHeld}</dd>
            {!reachedMaturity && (
              <>
                <dt>Rate paid</dt><dd>{closeQuote.effectiveRatePercent.toFixed(2)}% p.a. (after 1% penalty)</dd>
                <dt>Penalty</dt><dd>{money(closeQuote.penalty)}</dd>
              </>
            )}
            <dt>Interest</dt><dd>{money(closeQuote.interest)}</dd>
            <dt>You would receive</dt><dd><strong>{money(closeQuote.payout)}</strong></dd>
          </dl>
          {closeQuote.interestForfeited && (
            <div className="alert">Closing within 7 days pays no interest.</div>
          )}
          {close.error && <div className="alert error">{errorMessage(close.error)}</div>}

          {!confirming ? (
            <button className="btn danger" onClick={() => setConfirming(true)}>
              {reachedMaturity ? 'Close deposit' : 'Close early...'}
            </button>
          ) : (
            <div className="confirm">
              <p>
                {reachedMaturity
                  ? 'Close this deposit and credit the proceeds to your savings account?'
                  : `Closing early pays ${money(closeQuote.payout)} instead of ${money(fd.maturityAmount)} at maturity. This cannot be undone.`}
              </p>
              <button className="btn danger" disabled={close.isPending} onClick={() => close.mutate()}>
                {close.isPending ? 'Closing...' : 'Yes, close it'}
              </button>{' '}
              <button className="btn secondary" disabled={close.isPending} onClick={() => setConfirming(false)}>Cancel</button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}