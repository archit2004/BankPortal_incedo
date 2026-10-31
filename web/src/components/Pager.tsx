export function Pager({
  page, pageSize, total, onChange,
}: { page: number; pageSize: number; total: number; onChange: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="pager">
      <button className="btn secondary" disabled={page <= 1} onClick={() => onChange(page - 1)}>Previous</button>
      <span className="muted small">Page {page} of {pages} ({total} total)</span>
      <button className="btn secondary" disabled={page >= pages} onClick={() => onChange(page + 1)}>Next</button>
    </div>
  );
}