import type { Vehicle } from '../lib/types';
import { auctionTiming, currentPrice, reserveState } from '../lib/auction';
import { capitalize, formatCurrency, formatOdometer } from '../lib/format';
import { AuctionCountdown } from './AuctionCountdown';
import { ConditionBadge } from './ConditionBadge';
import { ReserveBadge } from './ReserveBadge';
import { TitleStatusBadge } from './TitleStatusBadge';
import { VehicleImage } from './VehicleImage';
import styles from './VehicleCard.module.css';

interface VehicleCardProps {
  vehicle: Vehicle;
  now: number;
  onSelect: (vehicle: Vehicle) => void;
  isHighBidder?: boolean;
  /** The buyer bought this vehicle via Buy Now — the auction is over. */
  isWon?: boolean;
}

export function VehicleCard({
  vehicle,
  now,
  onSelect,
  isHighBidder = false,
  isWon = false,
}: VehicleCardProps) {
  const timing = auctionTiming(vehicle, now);
  const hasBids = vehicle.current_bid !== null;
  const alt = `${vehicle.year} ${vehicle.make} ${vehicle.model}`;

  return (
    <article className={styles.card}>
      <div className={styles.media}>
        <VehicleImage src={vehicle.images[0]} alt={alt} fallbackLabel={alt} />
        <div className={styles.mediaOverlay}>
          {isWon ? (
            <span className={styles.soldChip}>Sold to you</span>
          ) : (
            <AuctionCountdown timing={timing} now={now} variant="overlay" />
          )}
          {isHighBidder && !isWon && <span className={styles.highBidder}>High bidder</span>}
        </div>
      </div>

      <div className={styles.body}>
        <header>
          <h3 className={styles.title}>
            <button type="button" className={styles.titleLink} onClick={() => onSelect(vehicle)}>
              {vehicle.year} {vehicle.make} {vehicle.model}
            </button>
          </h3>
          <p className={styles.subtitle}>
            {vehicle.trim} · {capitalize(vehicle.body_style)}
          </p>
        </header>

        <div className={styles.priceRow}>
          <div>
            <span className={styles.priceLabel}>
              {isWon ? 'Purchase price' : hasBids ? 'Current bid' : 'Starting bid'}
            </span>
            <span className={styles.price}>{formatCurrency(currentPrice(vehicle))}</span>
          </div>
          <span className={styles.bidCount}>
            {vehicle.bid_count} {vehicle.bid_count === 1 ? 'bid' : 'bids'}
          </span>
        </div>

        <div className={styles.badges}>
          <ConditionBadge grade={vehicle.condition_grade} />
          <TitleStatusBadge status={vehicle.title_status} />
          <ReserveBadge state={reserveState(vehicle)} />
        </div>

        <footer className={styles.meta}>
          <span>
            {vehicle.city}, {vehicle.province}
          </span>
          <span>{formatOdometer(vehicle.odometer_km)}</span>
        </footer>
      </div>
    </article>
  );
}
