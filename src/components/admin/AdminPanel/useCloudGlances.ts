// The rail's state for the Cloud infrastructure cards: each card's one tone and its headline, read
// from the same routes the cards read, so the rail shows how a resource is before anybody opens it
// (ADR: The onion cut into vertical slices, the CardShell). It is its own file because it is the
// rail's data, apart from how the rail is drawn.
import { useEffect, useState } from 'react';
import { type RailGlance } from '../../../lib/cloud';
import { GLANCED } from '../../../lib/railGlances';
import { type CardSlug } from '../../../lib/workbench';
import { request } from '../../../lib/request';

/** Each glanced card's tone and headline, read again on every tick; a card not read yet is absent. */
export function useCloudGlances(tick: number): Partial<Record<CardSlug, RailGlance>> {
  const [glances, setGlances] = useState<Partial<Record<CardSlug, RailGlance>>>({});
  useEffect(() => {
    let live = true;
    for (const { slug, route, glance } of GLANCED) {
      // The rail's glances are nobody's click: background, never the working lock.
      void request(route, undefined, { background: true })
        .then((r) =>
          r.ok ? (r.json() as Promise<unknown>) : Promise.reject(new Error(String(r.status)))
        )
        .then((answer) => {
          if (live) setGlances((was) => ({ ...was, [slug]: glance(answer) }));
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
