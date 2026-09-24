/**
 * Every page, checked (ADR: Every page, checked at every roll).
 */
import { useEffect, useState } from 'react';
import styles from '../AdminPanel.module.css';
import type { PageStatus, Fetched } from './types';
import { About } from './common';

/**
 * Every address this site serves, as the container found them (ADR: Every
 * page, checked at every roll). The sweep runs at every roll, so the reading
 * on this card belongs to the build the footer names, and the button runs one
 * now. Failures sort to the top because they are the only rows anybody needs
 * to read; the rest is there so the count can be checked rather than believed.
 */
export default function PagesCard({
  onReport,
}: {
  onReport: (seen: { checked: number; up: number }) => void;
}) {
  const [pages, setPages] = useState<Fetched<PageStatus>>(null);
  const [asked, setAsked] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const [showAll, setShowAll] = useState(false);

  useEffect(() => {
    let live = true;
    const read = () =>
      fetch('/api/admin/pages')
        .then((r) =>
          r.ok ? (r.json() as Promise<PageStatus>) : Promise.reject(new Error(String(r.status)))
        )
        .then((v) => {
          if (live) {
            setPages(v);
            if (v.report !== null) onReport({ checked: v.report.checked, up: v.report.up });
          }
          return v;
        })
        .catch(() => {
          if (live) setPages('failed');
          return null;
        });
    void read();
    // While a sweep is running the card follows it; ninety addresses take a
    // second or two, and a card that showed "running" until the next reload
    // would be the wrong answer for longer than the sweep takes.
    const timer = window.setInterval(() => {
      void read().then((v) => {
        if (v !== null && v.status !== 'running') {
          window.clearInterval(timer);
          setAsked(false);
        }
      });
    }, 3000);
    return () => {
      live = false;
      window.clearInterval(timer);
    };
  }, [asked, onReport]);

  const report = pages !== null && pages !== 'failed' ? pages.report : null;
  const down = report ? report.entries.filter((entry) => !entry.ok) : [];
  const shown = report
    ? showAll
      ? [...down, ...report.entries.filter((entry) => entry.ok)]
      : down
    : [];

  return (
    <article className={`${styles.wide} op-glass`} data-testid="pages-card">
      <h2 className={styles.cardTitle}>Every page, checked</h2>
      <About>
        This container asks itself for every address it serves and records what came back: the app,
        the API&rsquo;s own front pages, the three files at the root of the domain, every document
        in the catalogue and every drawing. The list is built from the catalogue rather than kept
        beside it, so a document added tomorrow is checked tomorrow. It runs at every roll, which is
        what makes the reading below belong to the build in the footer, and the button runs one now.
        An address that answers with nothing counts as down, and a document served as anything but
        markdown is a defect this card would have caught in the README weeks ago. This is the
        container&rsquo;s own view, dialled on its loopback; what the edge is serving the public is
        read from outside after every ship.
      </About>
      {pages === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : pages === 'failed' ? (
        <p className={styles.muted} data-testid="pages-failed">
          Could not read the page check on the last try.
        </p>
      ) : (
        <>
          <p className={styles.statusRow}>
            <button
              type="button"
              className={styles.back}
              onClick={() => {
                setNote(null);
                void fetch('/api/admin/pages', { method: 'POST' })
                  .then((r) => {
                    if (r.status === 409) {
                      return r
                        .json()
                        .then((body: { status?: string }) => setNote(body.status ?? 'not now'));
                    }
                    setAsked((a) => !a);
                    return undefined;
                  })
                  .catch(() => setNote('the check could not be started'));
              }}
              data-testid="pages-run"
            >
              Check every page now
            </button>
            {report !== null && (
              <button
                type="button"
                className={styles.back}
                aria-pressed={showAll}
                onClick={() => setShowAll((v) => !v)}
                data-testid="pages-show-all"
              >
                {showAll ? 'Show only what is down' : 'Show every address'}
              </button>
            )}
          </p>
          {note !== null && <p className={styles.muted}>{note}</p>}
          {report === null ? (
            <p className={styles.muted} data-testid="pages-not-run">
              {pages.status === 'running'
                ? 'Checking every address now…'
                : 'No check has run in this container yet. It runs at every roll, and the button above runs one now.'}
            </p>
          ) : report.failed !== null ? (
            <p className={styles.muted}>The last check stopped on {report.failed}.</p>
          ) : (
            <>
              <p data-testid="pages-summary">
                <strong>
                  {report.up} of {report.checked} addresses answered
                </strong>{' '}
                in {report.ms} ms, checked for {report.version} at {report.commit},{' '}
                {report.trigger === 'roll' ? 'on the roll' : 'when asked'}, at{' '}
                {new Date(report.at).toLocaleString()}.
              </p>
              {down.length === 0 && !showAll && (
                <p className={styles.muted} data-testid="pages-all-up">
                  Nothing is down.
                </p>
              )}
              {shown.length > 0 && (
                <div
                  className={styles.tableWrap}
                  role="region"
                  aria-label="Every address this container serves"
                  tabIndex={0}
                >
                  <table className={styles.table} data-testid="pages-table">
                    <thead>
                      <tr>
                        <th scope="col">Address</th>
                        <th scope="col">What</th>
                        <th scope="col">Kind</th>
                        <th scope="col">Answered</th>
                        <th scope="col">Type</th>
                        <th scope="col">Bytes</th>
                        <th scope="col">Took</th>
                      </tr>
                    </thead>
                    <tbody>
                      {shown.map((entry) => (
                        <tr key={entry.address}>
                          <td className={styles.mono}>{entry.address}</td>
                          <td>{entry.what}</td>
                          <td>{entry.kind}</td>
                          <td className={styles.mono}>
                            {entry.status === 0 ? (entry.reason ?? 'no answer') : entry.status}
                          </td>
                          <td className={styles.mono}>{entry.content_type ?? 'none'}</td>
                          <td className={styles.mono}>{entry.bytes.toLocaleString()}</td>
                          <td className={styles.mono}>{entry.ms} ms</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </>
          )}
        </>
      )}
    </article>
  );
}
