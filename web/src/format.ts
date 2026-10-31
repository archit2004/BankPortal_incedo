const inr = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' });
export const money = (n: number) => inr.format(n);

// "2026-10-31" -> "31 Oct 2026" (no timezone shift)
export function formatDate(iso: string): string {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' });
}

// The API sends UTC timestamps without a "Z", so add one before parsing.
export function formatDateTime(iso: string): string {
  const trimmed = iso.replace(/(\.\d{3})\d+/, '$1');
  const hasZone = /(Z|[+-]\d{2}:\d{2})$/i.test(trimmed);
  return new Date(hasZone ? trimmed : trimmed + 'Z').toLocaleString('en-IN', {
    timeZone: 'Asia/Kolkata', day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit',
  });
}

// Approximate maturity date for the preview (IST today + months, month-end clamped). The server's value is final.
export function previewMaturityDate(months: number): string {
  const t = new Date(Date.now() + 330 * 60000);
  const d = t.getUTCDate();
  const target = new Date(Date.UTC(t.getUTCFullYear(), t.getUTCMonth() + months, 1));
  const last = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate();
  target.setUTCDate(Math.min(d, last));
  return formatDate(target.toISOString().slice(0, 10));
}

export const isMoney = (s: string) => /^\d+(\.\d{1,2})?$/.test(s) && Number(s) > 0;

export const errorMessage = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.');