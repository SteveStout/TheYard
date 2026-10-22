/**
 * The one door a request leaves by. Every fetch the site makes is written in src/lib, inside
 * counted, so the working store (src/lib/working.ts) sees each one: a component asks here, with
 * its own init, and says background when nobody clicked for it (ADR: One working lock for the
 * whole site, after half a second, the addendum of the rings and slices).
 */
import { counted, type WorkingOptions } from './working';

/** A fetch through the working store: a person's request counts toward the lock, a background one never does. */
export function request(
  input: string,
  init?: RequestInit,
  options?: WorkingOptions
): Promise<Response> {
  return counted(() => fetch(input, init), options);
}
