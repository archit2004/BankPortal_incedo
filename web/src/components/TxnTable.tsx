import { Txn } from '../api';
import { formatDateTime, money } from '../format';

const labels: Record<string, string> = {
  Deposit: 'Deposit', Withdrawal: 'Withdrawal', FdOpen: 'FD opened', FdClose: 'FD closed', Interest: 'Interest',
};

export function TxnTable({ items }: { items: Txn[] }) {
  if (items.length === 0) return <p className="muted">No transactions yet.</p>;
  return (
    <div className="table-wrap">
      <table>
        <thead>
          <tr><th>Date</th><th>Type</th><th>Reference</th><th className="num">Amount</th><th className="num">Balance</th></tr>
        </thead>
        <tbody>
          {items.map((t) => (
            <tr key={t.id}>
              <td>{formatDateTime(t.createdAt)}</td>
              <td>{labels[t.type] ?? t.type}</td>
              <td className="muted">{t.reference ?? '-'}</td>
              <td className={`num ${t.amount < 0 ? 'neg' : 'pos'}`}>{money(t.amount)}</td>
              <td className="num">{money(t.balanceAfter)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}