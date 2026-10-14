/**
 * CODE HEALTH under the tests card's headline: the build's warnings, the
 * gate's seconds against five minutes, the page's built size and the packages
 * with a newer release, each with its tone, and the last gates behind More.
 * The rows come from src/lib/codeHealth.ts; this draws them.
 */
import { codeRows, type CodeNumbers, type GateSummary } from '../../../lib/codeHealth';
import cardStyles from '../shared/card.module.css';
import { TONE_WORDS, tonePill } from '../shared/common';
import { More } from '../shared/More';
import { DataTable } from '../DataTable';

export function CodeHealthSection({
  code,
  gateSeconds,
  history,
}: {
  code: CodeNumbers | undefined;
  gateSeconds: number;
  history: GateSummary[] | undefined;
}) {
  const rows = codeRows(code, gateSeconds);
  return (
    <section data-testid="code-health" aria-label="Code health">
      <h3 className={cardStyles.cardTitle}>Code health</h3>
      <ul className={cardStyles.checkList}>
        {rows.map((row) => (
          <li key={row.key} className={cardStyles.checkRow} data-testid={`code-${row.key}`}>
            {row.tone === 'plain' ? null : (
              <span className={tonePill(row.tone)}>{TONE_WORDS[row.tone]}</span>
            )}
            <span>{row.label}</span>
            <span className={cardStyles.muted}>{row.value}</span>
          </li>
        ))}
      </ul>
      {history !== undefined && history.length > 0 && (
        <More label={`the last ${history.length} gates`} testId="code-history">
          <DataTable
            label="The last gates"
            rows={[...history].reverse()}
            rowKey={(gate) => `${gate.version}-${gate.ranAt}`}
            columns={[
              { name: 'Version', cell: (gate) => gate.version },
              {
                name: 'Seconds',
                num: true,
                cell: (gate) => gate.gateSeconds.toLocaleString('en-US'),
              },
              { name: 'Passed', num: true, cell: (gate) => gate.passed.toLocaleString('en-US') },
              { name: 'Failed', num: true, cell: (gate) => gate.failed.toLocaleString('en-US') },
            ]}
          />
        </More>
      )}
    </section>
  );
}
