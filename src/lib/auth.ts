/**
 * The account seam (ADR: Accounts and per-user bids).
 *
 * Everything about a session that the browser is allowed to know goes through
 * here. What it never holds is the token: that lives in an httpOnly cookie the
 * page cannot read, so `credentials: 'same-origin'` is the whole of the client
 * side of authentication and there is nothing to store, refresh, or leak.
 */

export interface Account {
  signedIn: boolean;
  email: string | null;
  memberSinceMs: number | null;
}

export const SIGNED_OUT: Account = { signedIn: false, email: null, memberSinceMs: null };

export interface HistoryEntry {
  vehicleId: string;
  title: string;
  amount: number;
  outbid: boolean;
  wonBuyNow: boolean;
  highestAmount: number;
  atMs: number;
}

// #region auth-seam
interface AccountWire {
  signed_in: boolean;
  email: string | null;
  member_since_ms: number | null;
}

function toAccount(wire: AccountWire): Account {
  return {
    signedIn: wire.signed_in,
    email: wire.email,
    memberSinceMs: wire.member_since_ms,
  };
}

/**
 * The server's sentence, or a fallback. Every failure on this API answers
 * RFC 9457 ProblemDetails with the reason in `detail` (ADR: Error handling),
 * so a form can show what went wrong without the client knowing any rules.
 */
async function reason(response: Response, fallback: string): Promise<string> {
  try {
    const body = (await response.json()) as { detail?: unknown };
    return typeof body.detail === 'string' && body.detail.length > 0 ? body.detail : fallback;
  } catch {
    return fallback;
  }
}

export async function fetchAccount(signal?: AbortSignal): Promise<Account> {
  const response = await fetch('/api/auth/me', { signal, credentials: 'same-origin' });
  if (!response.ok) return SIGNED_OUT;
  return toAccount((await response.json()) as AccountWire);
}

export type AuthResult = { ok: true; account: Account } | { ok: false; message: string };

async function submit(url: string, email: string, password: string): Promise<AuthResult> {
  let response: Response;
  try {
    response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'same-origin',
      body: JSON.stringify({ email, password }),
    });
  } catch {
    return { ok: false, message: 'The server could not be reached. Try again in a moment.' };
  }
  if (!response.ok) {
    return { ok: false, message: await reason(response, 'That did not work. Try again.') };
  }
  return { ok: true, account: toAccount((await response.json()) as AccountWire) };
}

export function registerRequest(email: string, password: string): Promise<AuthResult> {
  return submit('/api/auth/register', email, password);
}

export function loginRequest(email: string, password: string): Promise<AuthResult> {
  return submit('/api/auth/login', email, password);
}

export async function logoutRequest(): Promise<Account> {
  try {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' });
  } catch {
    // Signing out is the one action where failing quietly is right: the token
    // expires on its own, and telling somebody their sign-out failed invites
    // them to keep clicking a button that cannot help them.
  }
  return SIGNED_OUT;
}
// #endregion auth-seam

interface HistoryWire {
  count: number;
  bids: {
    vehicle_id: string;
    title: string;
    bid: {
      amount: number;
      outbid: boolean;
      won_buy_now: boolean;
      highest_amount: number;
      at_ms: number;
    };
  }[];
}

export async function fetchHistory(signal?: AbortSignal): Promise<HistoryEntry[]> {
  const response = await fetch('/api/bids/history', { signal, credentials: 'same-origin' });
  if (!response.ok) return [];
  const wire = (await response.json()) as HistoryWire;
  return wire.bids.map((entry) => ({
    vehicleId: entry.vehicle_id,
    title: entry.title,
    amount: entry.bid.amount,
    outbid: entry.bid.outbid,
    wonBuyNow: entry.bid.won_buy_now,
    highestAmount: entry.bid.highest_amount,
    atMs: entry.bid.at_ms,
  }));
}
