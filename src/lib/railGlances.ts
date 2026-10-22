/**
 * Every card under Cloud infrastructure calculates a status (the owner's rule, ADR: One frame
 * for every resource, its addendum on every card's status): the rail beside the Admin tab shows a
 * dot and a headline for each, read from the same route the card reads. Eight answer in the
 * shared frame, where the server has already judged the tone; three draw their own body (Azure's
 * view of the container, what the machines are doing, memory against traffic), and their tone is
 * judged here from the same ranges their pills are painted with (ranges.ts), so the dot and the
 * card cannot disagree.
 *
 * A line that is not read never colours a dot: a reading that is absent leaves the tone to the
 * readings that are there, and a card with nothing read at all is unread.
 *
 * No React in here: the list and the three judgements are plain data and plain functions.
 */
import { BENCH_CARDS } from './bench';
import { railGlance, type CardTone, type CloudReport, type RailGlance } from './cloud';
import type { FollowsReport } from './follows';
import { toneOf, type RangeTone } from './ranges';
import { CLOUD_RESOURCES, type CardSlug } from './workbench';

/** The fields of GET /api/admin/azure the glance reads. */
export type AzureGlanceInput = {
  available: boolean;
  group_state?: string;
  container_state?: string;
  hourly?: {
    certificate: { days_left: number } | null;
    sql_storage: { share: number } | null;
  } | null;
};

/** The fields of GET /api/admin/machines the glance reads. */
export type MachinesGlanceInput = {
  container: { memory_limit_mb: number; samples: { working_set_mb: number }[] };
  relational: { available: boolean; rows: { memory_percent: number }[] };
};

/** The fields of GET /api/admin/follows the glance reads, from its own wire type. */
export type FollowsGlanceInput = Pick<FollowsReport, 'memory_limit_mb'> & {
  points: Pick<FollowsReport['points'][number], 'working_set_mb'>[];
  verdict: Pick<FollowsReport['verdict'], 'tone'>;
};

const RANK: Record<CardTone, number> = { unread: 0, fine: 1, amber: 2, red: 3 };

/** The worst of the tones that were read; unread only when none was. */
function worst(tones: CardTone[]): CardTone {
  return tones.reduce<CardTone>((was, tone) => (RANK[tone] > RANK[was] ? tone : was), 'unread');
}

/** A range's verdict as a card tone. */
function fromRange(tone: RangeTone): CardTone {
  return tone === 'bad' ? 'red' : tone === 'warn' ? 'amber' : 'fine';
}

const percent = (share: number) => `${Math.round(share * 100)} %`;

/** Azure's view of the container: its state, the certificate's days and the SQL store's share of its tier. */
export function azureGlance(state: AzureGlanceInput): RailGlance {
  if (!state.available) return { tone: 'unread', headline: null };
  const running = [state.group_state, state.container_state].filter(
    (s): s is string => s !== undefined
  );
  const tones: CardTone[] = running.map((s) => (s === 'Running' ? 'fine' : 'red'));
  const certificate = state.hourly?.certificate ?? null;
  if (certificate !== null) tones.push(fromRange(toneOf('certificateDays', certificate.days_left)));
  const storage = state.hourly?.sql_storage ?? null;
  if (storage !== null) tones.push(fromRange(toneOf('sqlStorage', storage.share)));
  return { tone: worst(tones), headline: state.group_state ?? null };
}

/** What the machines are doing: the container's newest memory against its limit, and the SQL store's own. */
export function machinesGlance(report: MachinesGlanceInput): RailGlance {
  const tones: CardTone[] = [];
  const newest = report.container.samples.at(-1);
  const limit = report.container.memory_limit_mb;
  const share = newest !== undefined && limit > 0 ? newest.working_set_mb / limit : null;
  if (share !== null) tones.push(fromRange(toneOf('memory', share)));
  const sql = report.relational.available ? report.relational.rows[0] : undefined;
  if (sql !== undefined) tones.push(fromRange(toneOf('memory', sql.memory_percent / 100)));
  return { tone: worst(tones), headline: share === null ? null : percent(share) };
}

/** Memory against traffic: the server's verdict on the window, with the newest memory against its limit. */
export function followsGlance(report: FollowsGlanceInput): RailGlance {
  const newest = report.points.at(-1);
  if (newest === undefined) return { tone: 'unread', headline: null };
  const verdict = report.verdict.tone;
  const tone: CardTone = verdict === 'alarm' ? 'red' : verdict === 'warning' ? 'amber' : 'fine';
  const share = report.memory_limit_mb > 0 ? newest.working_set_mb / report.memory_limit_mb : null;
  return { tone, headline: share === null ? null : percent(share) };
}

/** One glanced card: where it is read, and how its answer becomes a dot and a headline. */
export type Glanced = { slug: CardSlug; route: string; glance: (answer: unknown) => RailGlance };

/** Every card under Cloud infrastructure, in the rail's order, each with its route and its judgement. */
export const GLANCED: readonly Glanced[] = [
  {
    slug: 'plan',
    route: '/api/admin/plan?window=24h',
    glance: (a) => railGlance(a as CloudReport),
  },
  ...CLOUD_RESOURCES.filter((slug) => slug !== 'nightly').map((slug) => ({
    slug,
    route: `/api/admin/cloud/${slug}?window=24h`,
    glance: (a: unknown) => railGlance(a as CloudReport),
  })),
  {
    slug: 'azure',
    route: '/api/admin/azure',
    glance: (a) => azureGlance(a as AzureGlanceInput),
  },
  {
    slug: 'machines',
    route: '/api/admin/machines',
    glance: (a) => machinesGlance(a as MachinesGlanceInput),
  },
  {
    slug: 'follows',
    route: '/api/admin/follows',
    glance: (a) => followsGlance(a as FollowsGlanceInput),
  },
  {
    slug: 'nightly',
    route: '/api/admin/cloud/nightly?window=24h',
    glance: (a) => railGlance(a as CloudReport),
  },
];

/** The cards the rail files under Cloud infrastructure, which GLANCED must equal. */
export const CLOUD_SECTION: readonly CardSlug[] = BENCH_CARDS.filter(
  (card) => card.question === 'cloud'
).map((card) => card.slug);
