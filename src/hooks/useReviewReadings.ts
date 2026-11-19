/**
 * Does:      Reads the two answers the review route's first screen draws from, the tests summary and the
 *            recommendations, from the first load when the rendering service drew the page, else in the browser once.
 * Does not:  Pick a figure or a quote (reviewVerdict.ts does) or draw anything.
 * Used by:   ReviewPage.tsx.
 */
import { useEffect, useState } from 'react';
import { useFirstLoad } from './useFirstLoad';
import { TESTS_SUMMARY_URL, type TestSummary } from '../lib/landingProof';
import {
  RECOMMENDATIONS_URL,
  recommendationsFrom,
  type Recommendation,
} from '../lib/recommendations';
import { request } from '../lib/request';

/** An answer read in the background: the JSON on a 2xx, null on anything else, never a throw. */
function read(url: string): Promise<unknown> {
  return request(url, undefined, { background: true })
    .then((response) => (response.ok ? (response.json() as Promise<unknown>) : null))
    .catch(() => null);
}

/** A tests summary, or null when the answer is not one: the suites must be a list for the figure to be counted. */
function summaryFrom(answer: unknown): TestSummary | null {
  return typeof answer === 'object' &&
    answer !== null &&
    Array.isArray((answer as { suites?: unknown }).suites)
    ? (answer as TestSummary)
    : null;
}

/**
 * The two answers the review route's first screen draws from (ADR: The review route gives a reason before it asks):
 * the gate's tests summary for the live tile, and the recommendations for the quote and its count. A page the
 * rendering service drew carries both, so the server's draw and the browser's first draw agree and nothing moves;
 * a page the browser drew itself reads them once, in the background.
 */
export function useReviewReadings(): {
  summary: TestSummary | null;
  recommendations: Recommendation[] | null;
} {
  const first = useFirstLoad();
  const [summary, setSummary] = useState(() => summaryFrom(first?.tests ?? null));
  const [recommendations, setRecommendations] = useState(() =>
    recommendationsFrom(first?.recommendations ?? null)
  );
  const drawn = summary !== null && recommendations !== null;
  useEffect(() => {
    if (drawn) return;
    let live = true;
    void read(TESTS_SUMMARY_URL).then((answer) => {
      const next = summaryFrom(answer);
      if (live && next) setSummary(next);
    });
    void read(RECOMMENDATIONS_URL).then((answer) => {
      const next = recommendationsFrom(answer);
      if (live && next) setRecommendations(next);
    });
    return () => {
      live = false;
    };
  }, [drawn]);
  return { summary, recommendations };
}
