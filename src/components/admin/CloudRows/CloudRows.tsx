/**
 * A Cloud infrastructure card's four questions in the Overview card's row style (ADR: The App Service plan, read every minute): the question, the big answer with its tone word, and the detail line under
 * it, any piece of which links to a resource's portal blade. The colour sits on the border.
 */
import {
  portalSentence,
  toneWord,
  type CloudLink,
  type CloudReport,
  type CloudTone,
} from '../../../lib/cloud';
import { DataTable } from '../DataTable';
import rowStyles from '../OverviewCard/OverviewCard.module.css';
import styles from './CloudRows.module.css';

const TONE: Record<CloudTone, string> = {
  good: rowStyles.good,
  info: rowStyles.plain,
  warning: rowStyles.warn,
  alarm: rowStyles.bad,
};

/** A resource name linked to its portal blade, or plain text when Azure did not hand back what the link needs. */
export function PortalLink({ link }: { link: CloudLink }) {
  if (link.href === null) return <>{link.text}</>;
  return (
    <a className={styles.portal} href={link.href} target="_blank" rel="noreferrer noopener">
      {link.text}
      <span aria-hidden="true">↗</span>
      <span className={styles.hidden}> {portalSentence(link.href, true)}</span>
    </a>
  );
}

export function CloudRows({ rows }: { rows: CloudReport['rows'] }) {
  return (
    <div className={rowStyles.rows} data-testid="cloud-rows">
      {rows.map((row) => (
        <div
          key={row.key}
          className={`${rowStyles.row} ${TONE[row.tone]}`}
          data-testid={`cloud-row-${row.key}`}
          data-tone={row.tone}
        >
          <span className={rowStyles.question}>
            {row.question}
            {row.source !== null && <span className={styles.source}>{row.source}</span>}
          </span>
          <span className={rowStyles.value}>
            {row.answer} <span className={styles.word}>{toneWord(row.tone)}</span>
          </span>
          {row.detail.length > 0 && (
            <span className={styles.detail}>
              {row.detail.map((piece, index) => (
                <PortalLink key={index} link={piece} />
              ))}
            </span>
          )}
          {row.table.rows.length > 0 && (
            <div className={styles.table} data-testid={`cloud-table-${row.key}`}>
              <DataTable
                label={row.question}
                columns={row.table.columns.map((name, at) => ({
                  name,
                  cell: (cells: CloudLink[]) => (
                    <PortalLink link={cells[at] ?? { text: '', href: null }} />
                  ),
                  rowHeader: at === 0,
                }))}
                rows={row.table.rows}
                rowKey={(cells, index) => `${cells[0]?.text ?? ''}-${index}`}
              />
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
