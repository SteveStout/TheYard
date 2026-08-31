import { CONDITION_BAND_LABELS, conditionBand } from '../lib/condition';
import styles from './ConditionBadge.module.css';

interface ConditionBadgeProps {
  grade: number;
  size?: 'sm' | 'lg';
}

/** Numeric condition grade plus its band label, colour-coded by band. */
export function ConditionBadge({ grade, size = 'sm' }: ConditionBadgeProps) {
  const band = conditionBand(grade);
  return (
    <span
      className={`${styles.badge} ${styles[band]} ${size === 'lg' ? styles.lg : ''}`}
      title={`Condition grade ${grade.toFixed(1)} out of 5`}
    >
      <strong className={styles.grade}>{grade.toFixed(1)}</strong>
      {CONDITION_BAND_LABELS[band]}
    </span>
  );
}
