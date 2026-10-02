import { CONDITION_BAND_LABELS, conditionBand } from '../../../lib/condition';
import styles from './ConditionBadge.module.css';

interface ConditionBadgeProps {
  grade: number;
  size?: 'sm' | 'lg';
}

/**
 * Numeric condition grade plus its band label, colour-coded by band. The
 * grade is written over its scale ("3.4/5") on the badge itself, because a
 * bare 3.4 means nothing until the reader knows the top of the scale, and a
 * hover title is not there on a phone.
 */
export function ConditionBadge({ grade, size = 'sm' }: ConditionBadgeProps) {
  const band = conditionBand(grade);
  return (
    <span
      className={`${styles.badge} ${styles[band]} ${size === 'lg' ? styles.lg : ''}`}
      title={`Condition grade ${grade.toFixed(1)} out of 5`}
    >
      <strong className={styles.grade}>{grade.toFixed(1)}/5</strong>
      {CONDITION_BAND_LABELS[band]}
    </span>
  );
}
