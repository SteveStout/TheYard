/**
 * Does:      Reads the author document into its laid-out parts for the About page: from the first load when the rendering
 *            service drew the page, else in the browser once, the document and the code that renders it asked for together.
 * Does not:  Place a part or draw anything (AboutPage.tsx does) or hold the layout (author.ts does).
 * Used by:   AboutPage.tsx.
 */
import { useEffect, useState } from 'react';
import { DOCS } from '../library/documents';
import type { AuthorParts } from '../lib/author';
import { request } from '../lib/request';
import { useFirstLoad } from './useFirstLoad';

/** The parts, null while they are read, or 'failed' when the document could not be read. */
export type AuthorReading = AuthorParts | null | 'failed';

export function useAuthorParts(): AuthorReading {
  const drawn = useFirstLoad()?.author ?? null;
  const [parts, setParts] = useState<AuthorReading>(drawn);
  useEffect(() => {
    if (drawn !== null) return;
    let live = true;
    // The renderer and the layout are chunks of their own, as the document window asked for them (DocDialog.tsx).
    Promise.all([
      request(DOCS.author.url).then(async (response) => {
        if (!response.ok) throw new Error(String(response.status));
        return response.text();
      }),
      import('../lib/markdown'),
      import('../lib/author'),
    ])
      .then(async ([markdown, { renderDocument }, { authorParts }]) =>
        authorParts(await renderDocument(markdown))
      )
      .then(
        (next) => live && setParts(next),
        () => live && setParts('failed')
      );
    return () => {
      live = false;
    };
  }, [drawn]);
  return parts;
}
