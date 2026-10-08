import { describe, expect, it } from 'vitest';
import type { FirstLoad } from '../hooks/useFirstLoad';
import type { Vehicle } from '../lib/types';
import { drawForTheService } from './entry-server';

// #region fixtures
const NOW = Date.UTC(2026, 9, 8, 15, 0, 0);

/**
 * Three vehicles as the API writes them, written out here rather than read from data/: the image's
 * frontend stage builds from src/ and public/ only, and a test that reaches outside them breaks the
 * build that ships (DockerBuildInputsTests).
 */
const vehicles: Vehicle[] = [
  ['4e3cd74f-bb88-4efe-b234-bcb2f7474b40', 'CG2UAF4T8LRBBVWJY', 'Mazda', 'CX-5', 20_500],
  ['7d0f5c1e-1a2b-4c3d-9e8f-0a1b2c3d4e5f', '1FTFW1E50NFA00001', 'Ford', 'F-150', 31_000],
  ['b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e', '2T1BURHE0KC000002', 'Toyota', 'Corolla', 12_800],
].map(([id, vin, make, model, startingBid], index): Vehicle => ({
  id: String(id),
  vin: String(vin),
  year: 2024,
  make: String(make),
  model: String(model),
  trim: 'Base',
  body_style: 'SUV',
  exterior_color: 'Blue',
  interior_color: 'Black',
  engine: '2.5L I4',
  transmission: 'automatic',
  drivetrain: 'FWD',
  odometer_km: 24_534,
  fuel_type: 'gasoline',
  condition_grade: 4,
  condition_report: `A clean ${String(make)}: minor cosmetic wear only.`,
  damage_notes: [],
  title_status: 'clean',
  province: 'Ontario',
  city: 'Mississauga',
  auction_start: '2026-10-08T15:00:00',
  starting_bid: Number(startingBid),
  buy_now_price: null,
  images: [`https://placehold.co/800x600?text=${String(model)}`],
  selling_dealership: 'A dealer',
  lot: `L-${index + 1}`,
  current_bid: null,
  bid_count: 0,
  auction_starts_at: NOW - 3_600_000,
  auction_ends_at: NOW + (index + 1) * 600_000,
  auction_status: 'live',
  min_next_bid: Number(startingBid),
  reserve_state: 'not-met',
  sold: false,
}));

/** What the service reads for an address, with nothing but the address and the clock filled in. */
function load(search: string, more: Partial<FirstLoad> = {}): FirstLoad {
  return {
    search,
    nowMs: NOW,
    account: { signedIn: false, email: null, memberSinceMs: null },
    build: { version: '1.0.3.100', commit: 'abc1234' },
    listing: null,
    vehicle: null,
    doc: null,
    ...more,
  };
}

const listing = {
  page: { total: 100_000, vehicles },
  facets: {
    makes: ['Mazda'],
    body_styles: ['SUV'],
    title_statuses: ['clean'],
    provinces: ['Ontario'],
  },
};

/** The page drawn for the service, read to its end as text. */
async function draw(first: FirstLoad): Promise<string> {
  const errors: unknown[] = [];
  const html = await new Response(await drawForTheService(first, (e) => errors.push(e))).text();
  expect(errors).toEqual([]);
  return html;
}
// #endregion fixtures

// #region each-view
// Each view the rendering service draws, drawn here with no window, the way drawLanding.test.ts
// draws the landing page: a component that reads the browser while it draws fails here and not on
// the live site (ADR: A rendering service beside the API).
describe('the views the rendering service draws, with no browser', () => {
  it('has no window, so these are the rules a server puts on every component', () => {
    expect(typeof (globalThis as { window?: unknown }).window).toBe('undefined');
  });

  it('draws the landing page, with the build the API reported in the footer', async () => {
    const html = await draw(load(''));
    expect(html).toContain('Welcome to The Yard');
    expect(html).toContain('1.0.3.100');
  });

  it('draws the inventory with its first page of vehicles, not a loading line', async () => {
    const html = await draw(load('view=inventory', { listing }));
    expect(html).not.toContain('Loading inventory');
    for (const vehicle of vehicles) expect(html).toContain(vehicle.model);
  });

  it('draws the loading line when the list did not arrive in time, and the browser asks for it', async () => {
    const html = await draw(load('view=inventory'));
    expect(html).toContain('Loading inventory');
  });

  it('draws a vehicle over the list it opened from', async () => {
    const [vehicle] = vehicles;
    const html = await draw(load(`vehicle=${vehicle.id}`, { listing, vehicle }));
    expect(html).toContain(vehicle.vin);
    expect(html).toContain(vehicle.condition_report);
  });

  it('draws a document open in its window, already rendered', async () => {
    const doc = { key: 'readme', html: '<h2 id="in-plain-words">In plain words</h2><p>Drawn.</p>' };
    const html = await draw(load('doc=readme', { doc }));
    expect(html).toMatch(/<dialog[^>]* open=""/);
    expect(html).toContain('<p>Drawn.</p>');
    expect(html).not.toContain('Loading...');
  });

  it('draws the Author page in its own wide window', async () => {
    const doc = { key: 'author', html: '<section><h2>About Steven</h2></section>' };
    const html = await draw(load('doc=author', { doc }));
    expect(html).toMatch(
      /<dialog[^>]* open=""[^>]*aria-label="About Steven"|<dialog[^>]*aria-label="About Steven"[^>]* open=""/
    );
    expect(html).toContain('<section><h2>About Steven</h2></section>');
  });

  it('leaves Admin and the account page to the browser, behind the frame', async () => {
    expect(await draw(load('view=admin'))).toContain('Reading the machines...');
    expect(await draw(load('view=account'))).toContain('Reading your account...');
  });

  it('draws a signed-in visitor as signed in', async () => {
    const account = { signedIn: true, email: 'reader@example.com', memberSinceMs: NOW };
    const html = await draw(load('view=inventory', { listing, account }));
    expect(html).toContain('reader@example.com');
  });
});
// #endregion each-view
