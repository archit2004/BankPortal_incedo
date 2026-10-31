// API client. The access token lives in memory only; the refresh token lives in sessionStorage.
// Refresh tokens rotate, so concurrent refreshes MUST share one request (otherwise the server
// sees a reused token and revokes the whole session).

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

const RT_KEY = 'bp.refresh';
let accessToken: string | null = null;
let refreshInFlight: Promise<boolean> | null = null;
let onSessionExpired: (() => void) | null = null;

export function setSessionExpiredHandler(fn: () => void) {
  onSessionExpired = fn;
}
export const getAccessToken = () => accessToken;
export const hasRefreshToken = () => !!sessionStorage.getItem(RT_KEY);

export interface AuthResult {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
}

function storeTokens(r: AuthResult) {
  accessToken = r.accessToken;
  sessionStorage.setItem(RT_KEY, r.refreshToken);
}

export function clearTokens() {
  accessToken = null;
  sessionStorage.removeItem(RT_KEY);
}

async function parseError(res: Response): Promise<ApiError> {
  let message = `Request failed (${res.status}).`;
  try {
    const body = await res.json();
    if (body?.errors && typeof body.errors === 'object') {
      message = (Object.values(body.errors) as string[][]).flat().join(' ');
    } else if (body?.title) {
      message = body.title;
    }
  } catch {
    // body was not JSON
  }
  if (res.status === 403) message = 'You are not allowed to do that.';
  if (res.status === 429) message = 'Too many requests. Please wait a moment and try again.';
  return new ApiError(res.status, message);
}

async function send(
  path: string,
  method: string,
  body?: unknown,
  token?: string | null,
  extra?: Record<string, string>,
): Promise<Response> {
  const headers: Record<string, string> = { ...(extra ?? {}) };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (token) headers.Authorization = `Bearer ${token}`;
  try {
    return await fetch(path, {
      method,
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    throw new ApiError(0, 'Cannot reach the server. Check your connection and try again.');
  }
}

export function refreshSession(): Promise<boolean> {
  if (refreshInFlight) return refreshInFlight;
  const rt = sessionStorage.getItem(RT_KEY);
  if (!rt) return Promise.resolve(false);

  refreshInFlight = (async () => {
    try {
      const res = await send('/api/auth/refresh', 'POST', { refreshToken: rt });
      if (!res.ok) {
        clearTokens();
        return false;
      }
      storeTokens((await res.json()) as AuthResult);
      return true;
    } catch {
      return false; // network problem: keep the stored token and let the caller decide
    } finally {
      refreshInFlight = null;
    }
  })();
  return refreshInFlight;
}

export async function api<T>(
  path: string,
  opts: { method?: string; body?: unknown; headers?: Record<string, string> } = {},
): Promise<T> {
  const method = opts.method ?? 'GET';
  let res = await send(path, method, opts.body, accessToken, opts.headers);

  if (res.status === 401) {
    const refreshed = await refreshSession();
    if (refreshed) res = await send(path, method, opts.body, accessToken, opts.headers);
    if (!refreshed || res.status === 401) {
      clearTokens();
      onSessionExpired?.();
      throw new ApiError(401, 'Your session has expired. Please sign in again.');
    }
  }

  if (!res.ok) throw await parseError(res);
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

async function authCall(path: string, body: unknown): Promise<AuthResult> {
  const res = await send(path, 'POST', body);
  if (!res.ok) throw await parseError(res);
  const result = (await res.json()) as AuthResult;
  storeTokens(result);
  return result;
}

export const login = (email: string, password: string) =>
  authCall('/api/auth/login', { email, password });

export const register = (fullName: string, email: string, password: string) =>
  authCall('/api/auth/register', { fullName, email, password });

export async function logout() {
  const rt = sessionStorage.getItem(RT_KEY);
  clearTokens();
  if (rt) {
    try {
      await send('/api/auth/logout', 'POST', { refreshToken: rt });
    } catch {
      // logout is best effort
    }
  }
}

// ---------- DTO types (mirror the API) ----------
export interface Account { id: string; accountNumber: string; balance: number; status: string }
export interface Txn {
  id: string; type: string; amount: number; balanceAfter: number; reference: string | null; createdAt: string;
}
export interface DashboardData { account: Account; recentTransactions: Txn[] }
export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number }
export interface Rate {
  minTenureMonths: number; maxTenureMonths: number; annualRatePercent: number; effectiveFrom: string;
}
export interface Fd {
  id: string; accountId: string; principal: number; annualRatePercent: number; tenureMonths: number;
  compoundingPerYear: number; startDate: string; maturityDate: string; maturityAmount: number;
  status: string; closedOn: string | null; penaltyApplied: number | null;
}
export interface CloseQuote {
  payout: number; interest: number; penalty: number; effectiveRatePercent: number;
  daysHeld: number; interestForfeited: boolean; asOf: string;
}
export interface FdDetailData { fd: Fd; closeQuote: CloseQuote | null }
export interface CloseResult {
  fd: Fd; payout: number; interest: number; penalty: number; transactionId: string; balanceAfter: number;
}
export interface AdminCustomer {
  id: string; fullName: string; email: string; createdAt: string; accountCount: number;
  savingsBalance: number; activeFdCount: number; activeFdPrincipal: number; totalDeposits: number;
}
export interface AdminFd {
  id: string; customerName: string; accountNumber: string; principal: number; annualRatePercent: number;
  tenureMonths: number; startDate: string; maturityDate: string; daysToMaturity: number | null;
  maturityAmount: number; status: string;
}
export interface DepositsReport {
  asOf: string; customerCount: number; accountCount: number; savingsBalance: number;
  activeFdCount: number; activeFdPrincipal: number; totalDeposits: number; accruedInterestOnActiveFds: number;
}
export interface JobRunResult {
  date: string; fdsProcessed: number; accrualsCreated: number; interestAccrued: number;
  fdsMatured: number; maturityPaidOut: number; failures: number;
}