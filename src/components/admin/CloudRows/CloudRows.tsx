/**
 * A Cloud infrastructure card's four questions in the Overview card's row style (ADR: The App Service plan, read every minute): the question, the big answer with its tone word, and the detail line under
 * it, any piece of which links to a resource's portal blade. The colour sits on the border.
 */
import { toneWord, type CloudLink, type CloudReport, type CloudTone } from '../../../lib/cloud';
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
      <span className={styles.hidden}>
        {' '}
        (opens the Azure portal in a new tab, with your own Azure sign-in)
      </span>
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
          <span className={rowStyles.question}>{row.question}</span>
          <span className={rowStyles.value}>
            {row.answer} <span className={styles.word}>{toneWord(row.tone)}</span>
          </span>
          <span className={styles.detail}>
            {row.detail.map((piece, index) => (
              <PortalLink key={index} link={piece} />
            ))}
          </span>
        </div>
      ))}
    </div>
  );
}
