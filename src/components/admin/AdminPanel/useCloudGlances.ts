// The rail's state for the Cloud infrastructure cards: each card's one tone and its headline, read
// from the same routes the cards read, so the rail shows how a resource is before anybody opens it
// (ADR: The onion cut into vertical slices, the CardShell). It is its own file because it is the
// rail's data, apart from how the rail is drawn.
import { useEffect, useState } from 'react';
import { railGlance, type CloudReport, type RailGlance } from '../../../lib/cloud';
import type { CardSlug } from '../../../lib/workbench';

/** The resource cards after the plan, each answering on /api/admin/cloud/{slug}. */
const RESOURCES = ['sqldb', 'cosmosdb', 'registry', 'insights', 'mail', 'edge', 'nightly'] as const;

/** The cards the rail reads a glance for, and the route each answers on. */
export const GLANCED: readonly { slug: CardSlug; route: string }[] = [
  { slug: 'plan', route: '/api/admin/plan?window=24h' },
  ...RESOURCES.map((slug) => ({
    slug,
    route: `/api/admin/cloud/${slug}?window=24h`,
  })),
];

/** Each glanced card's tone and headline, read again on every tick; a card not read yet is absent. */
export function useCloudGlances(tick: number): Partial<Record<CardSlug, RailGlance>> {
  const [glances, setGlances] = useState<Partial<Record<CardSlug, RailGlance>>>({});
  useEffect(() => {
    let live = true;
    for (const { slug, route } of GLANCED) {
      void fetch(route)
        .then((r) =>
          r.ok ? (r.json() as Promise<CloudReport>) : Promise.reject(new Error(String(r.status)))
        )
        .then((report) => {
          if (live) setGlances((was) => ({ ...was, [slug]: railGlance(report) }));
        })
        .catch(() => {
          if (live) setGlances((was) => ({ ...was, [slug]: { tone: 'unread', headline: null } }));
        });
    }
    return () => {
      live = false;
    };
  }, [tick]);
  return glances;
}
