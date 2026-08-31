import styles from './TitleStatusBadge.module.css';

interface TitleStatusBadgeProps {
  status: string;
  size?: 'sm' | 'lg';
}

/** "clean" is reassuring; "rebuilt" and "salvage" carry warning weight. */
const TONE_BY_STATUS: Record<string, string> = {
  clean: styles.clean,
  rebuilt: styles.caution,
  salvage: styles.danger,
};

export function TitleStatusBadge({ status, size = 'sm' }: TitleStatusBadgeProps) {
  const tone = TONE_BY_STATUS[status] ?? styles.neutral;
  const label = status.charAt(0).toUpperCase() + status.slice(1);
  return (
    <span className={`${styles.badge} ${tone} ${size === 'lg' ? styles.lg : ''}`}>
      {label} title
    </span>
  );
}
