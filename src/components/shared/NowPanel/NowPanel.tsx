/**
 * Does:      Draws the sales pages' dark live panel (ADR: The sales pages set the site's look), as the sales1120 picture
 *            draws it: a title and, when it is read live, the LIVE tag with its dot; then either rows (a label, a value
 *            and a line under the value, each value hooked as a live value) or a two-by-two block of facts; and "as of
 *            HH:MM" under the rows when the newest read failed. A panel with no row to draw is not drawn at all.
 * Does not:  Read anything or pick a row (nowPanel.ts does, and hides a row never read), or hold its look (sales.css,
 *            op-live-panel, does). Named NowPanel because components/admin/LivePanel already holds that name.
 * Used by:   ReviewVerdict.tsx, CaseCard.tsx.
 */
import type { NowRow } from '../../../lib/nowPanel';

export function NowPanel({
  title,
  live = false,
  rows,
  facts,
  asOf = null,
  hook,
  id,
  as: Tag = 'section',
}: {
  title: string;
  /** Read live from the running site: the LIVE tag is drawn beside the title. */
  live?: boolean;
  /** The rows, each read; none drawn means no panel. */
  rows?: readonly NowRow[];
  /** The stack, as label and value pairs, instead of rows. */
  facts?: readonly (readonly [string, string])[];
  /** "as of HH:MM" when the newest read failed and the panel shows the last answer. */
  asOf?: string | null;
  /** The check's hook: live-panel on a first screen, case-panel on a case card. */
  hook: 'live-panel' | 'case-panel';
  id?: string;
  /** A section of its own, or an aside beside a case card's body. */
  as?: 'section' | 'aside';
}) {
  if (!facts && (!rows || rows.length === 0)) return null;
  return (
    <Tag className="op-live-panel" id={id} aria-label={title} data-mock={hook}>
      <p className="op-live-head">
        <span>{title}</span>
        {live && (
          <span className="op-live-tag">
            <i className="op-live-dot" aria-hidden="true" />
            Live
          </span>
        )}
      </p>
      {rows?.map((row) => (
        <p key={row.key} className="op-live-row" data-row={row.key}>
          <span>{row.label}</span>
          <b data-mock="live-value">
            {row.value}
            <small>{row.under}</small>
          </b>
        </p>
      ))}
      {facts && (
        <dl className="op-live-facts">
          {facts.map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
      {asOf && <p className="op-live-foot">{asOf}</p>}
    </Tag>
  );
}
