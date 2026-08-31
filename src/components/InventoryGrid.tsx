import type { Vehicle } from '../lib/types';
import { VehicleCard } from './VehicleCard';
import styles from './InventoryGrid.module.css';

interface InventoryGridProps {
  vehicles: Vehicle[];
  now: number;
  onSelect: (vehicle: Vehicle) => void;
  highBidderIds?: ReadonlySet<string>;
  wonIds?: ReadonlySet<string>;
  onClearFilters?: () => void;
}

/** Responsive card grid: 3 across on desktop, 2 on tablet, 1 on mobile. */
export function InventoryGrid({
  vehicles,
  now,
  onSelect,
  highBidderIds,
  wonIds,
  onClearFilters,
}: InventoryGridProps) {
  if (vehicles.length === 0) {
    return (
      <div className={styles.empty}>
        <p className={styles.emptyTitle}>No vehicles match your filters</p>
        <p className={styles.emptyHint}>Try widening the price range or clearing a filter.</p>
        {onClearFilters && (
          <button type="button" className={styles.emptyAction} onClick={onClearFilters}>
            Clear all filters
          </button>
        )}
      </div>
    );
  }

  return (
    <ul className={styles.grid}>
      {vehicles.map((vehicle) => (
        <li key={vehicle.id}>
          <VehicleCard
            vehicle={vehicle}
            now={now}
            onSelect={onSelect}
            isHighBidder={highBidderIds?.has(vehicle.id) ?? false}
            isWon={wonIds?.has(vehicle.id) ?? false}
          />
        </li>
      ))}
    </ul>
  );
}
