import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  fetchAccount,
  fetchHistory,
  loginRequest,
  logoutRequest,
  registerRequest,
  SIGNED_OUT,
} from './auth';

/**
 * The account seam (ADR: Accounts and per-user bids). What is worth holding
 * here is the part the browser is allowed to know: the wire's snake_case
 * becomes the app's camelCase, a refusal becomes the server's own sentence,
 * and nothing anywhere holds a token.
 */

/** One canned response, with the pieces vitest needs from a real one. */
function reply(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response;
}

const fetchMock = vi.fn();

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('fetchAccount', () => {
  it('translates the wire into the account the page uses', async () => {
    fetchMock.mockResolvedValue(
      reply(200, { signed_in: true, email: 'a@example.com', member_since_ms: 1_700_000_000_000 })
    );

    await expect(fetchAccount()).resolves.toEqual({
      signedIn: true,
      email: 'a@example.com',
      memberSinceMs: 1_700_000_000_000,
    });
  });

  it('reads signed out as signed out rather than as an error', async () => {
    fetchMock.mockResolvedValue(reply(401, {}));
    await expect(fetchAccount()).resolves.toEqual(SIGNED_OUT);
  });
});

describe('registerRequest and loginRequest', () => {
  it('sends the credentials as JSON and never asks for a token back', async () => {
    fetchMock.mockResolvedValue(
      reply(200, { signed_in: true, email: 'a@example.com', member_since_ms: 1 })
    );

    const result = await registerRequest('a@example.com', 'correct horse');

    expect(result).toEqual({
      ok: true,
      account: { signedIn: true, email: 'a@example.com', memberSinceMs: 1 },
    });
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('/api/auth/register');
    expect(init.credentials).toBe('same-origin');
    expect(JSON.parse(init.body as string)).toEqual({
      email: 'a@example.com',
      password: 'correct horse',
    });
  });

  it('shows the server its own sentence when a sign-in is refused', async () => {
    fetchMock.mockResolvedValue(
      reply(401, { detail: 'That email address and password do not match an account.' })
    );

    await expect(loginRequest('a@example.com', 'wrong')).resolves.toEqual({
      ok: false,
      message: 'That email address and password do not match an account.',
    });
  });

  it('falls back to its own wording when the body carries no detail', async () => {
    fetchMock.mockResolvedValue(reply(400, {}));
    const result = await loginRequest('a@example.com', 'short');
    expect(result).toEqual({ ok: false, message: 'That did not work. Try again.' });
  });

  it('says the server is unreachable rather than throwing at the form', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    const result = await registerRequest('a@example.com', 'correct horse');
    expect(result).toEqual({
      ok: false,
      message: 'The server could not be reached. Try again in a moment.',
    });
  });
});

describe('logoutRequest', () => {
  it('answers signed out', async () => {
    fetchMock.mockResolvedValue(reply(200, {}));
    await expect(logoutRequest()).resolves.toEqual(SIGNED_OUT);
  });

  it('answers signed out even when the request fails, because it will expire anyway', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(logoutRequest()).resolves.toEqual(SIGNED_OUT);
  });
});

describe('fetchHistory', () => {
  it('flattens the wire entry into one row per bid', async () => {
    fetchMock.mockResolvedValue(
      reply(200, {
        count: 1,
        bids: [
          {
            vehicle_id: 'v-1',
            title: '2019 Ford Bronco',
            bid: {
              amount: 12_500,
              outbid: true,
              won_buy_now: false,
              highest_amount: 13_000,
              at_ms: 1_700_000_000_000,
            },
          },
        ],
      })
    );

    await expect(fetchHistory()).resolves.toEqual([
      {
        vehicleId: 'v-1',
        title: '2019 Ford Bronco',
        amount: 12_500,
        outbid: true,
        wonBuyNow: false,
        highestAmount: 13_000,
        atMs: 1_700_000_000_000,
      },
    ]);
  });

  it('answers an empty list when the caller is not signed in', async () => {
    fetchMock.mockResolvedValue(reply(401, {}));
    await expect(fetchHistory()).resolves.toEqual([]);
  });
});
