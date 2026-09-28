import styles from './Readout.module.css';

/**
 * A reading, not prose (ADR: The glass look, the addendum on the operator's
 * look): a label and its value on one line, in small spaced capitals with
 * tabular figures, a hairline under each, so a column of numbers lines up.
 * Across (`across`) it is one row of pairs, for a strip under a title.
 */
export function Readout({
  rows,
  across = false,
  testId,
}: {
  rows: ReadonlyArray<readonly [label: string, value: string]>;
  across?: boolean;
  testId?: string;
}) {
  return (
    <dl className={`${styles.readout} ${across ? styles.across : ''}`} data-testid={testId}>
      {rows.map(([label, value]) => (
        <div key={label} className={styles.row}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}
