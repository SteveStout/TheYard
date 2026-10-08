import { useInTheBrowser } from '../../../hooks/useFirstLoad';
import type { AuctionTiming } from '../../../lib/auction';
import { formatAuctionDateTime, formatCountdown } from '../../../lib/format';
import styles from './AuctionCountdown.module.css';

interface AuctionCountdownProps {
  timing: AuctionTiming;
  now: number;
  /** "overlay" renders on top of imagery (dark chip); "inline" sits in text. */
  variant?: 'inline' | 'overlay';
}

/** Live/upcoming/ended indicator with its countdown, on one shared clock. */
export function AuctionCountdown({ timing, now, variant = 'inline' }: AuctionCountdownProps) {
  const { status, startsAt, endsAt } = timing;
  // The viewer's zone once the page is the browser's; UTC while a server-drawn page is taken over.
  const inViewersZone = useInTheBrowser();

  const label =
    status === 'live'
      ? `Live · ${formatCountdown(endsAt, now)} left`
      : status === 'upcoming'
        ? `Starts in ${formatCountdown(startsAt, now)}`
        : `Ended ${formatAuctionDateTime(endsAt, inViewersZone)}`;

  return (
    <span
      className={`${styles.countdown} ${styles[status]} ${variant === 'overlay' ? styles.overlay : ''}`}
    >
      <span className={styles.dot} aria-hidden="true" />
      {label}
    </span>
  );
}
