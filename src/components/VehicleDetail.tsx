import { useState } from 'react';
import type { Vehicle } from '../lib/types';
import { auctionTiming, reserveState } from '../lib/auction';
import type { BidOutcome } from '../lib/data';
import { capitalize, formatOdometer } from '../lib/format';
import { AuctionCountdown } from './AuctionCountdown';
import { BidPanel } from './BidPanel';
import { ConditionBadge } from './ConditionBadge';
import { ReserveBadge } from './ReserveBadge';
import { TitleStatusBadge } from './TitleStatusBadge';
import { VehicleImage } from './VehicleImage';
import styles from './VehicleDetail.module.css';

interface VehicleDetailProps {
  vehicle: Vehicle;
  now: number;
  onBack: () => void;
  isHighBidder: boolean;
  wonBuyNow: boolean;
  onPlaceBid: (amount: number) => Promise<BidOutcome>;
  onBuyNow: () => Promise<BidOutcome>;
}

export function VehicleDetail({
  vehicle,
  now,
  onBack,
  isHighBidder,
  wonBuyNow,
  onPlaceBid,
  onBuyNow,
}: VehicleDetailProps) {
  const [imageIndex, setImageIndex] = useState(0);
  const timing = auctionTiming(vehicle, now);
  const alt = `${vehicle.year} ${vehicle.make} ${vehicle.model}`;
  const mainImage = vehicle.images[imageIndex] ?? vehicle.images[0];

  const specs: Array<[string, string]> = [
    ['Engine', vehicle.engine],
    ['Transmission', capitalize(vehicle.transmission)],
    ['Drivetrain', vehicle.drivetrain],
    ['Fuel type', capitalize(vehicle.fuel_type)],
    ['Odometer', formatOdometer(vehicle.odometer_km)],
    ['Exterior', vehicle.exterior_color],
    ['Interior', vehicle.interior_color],
    ['VIN', vehicle.vin],
    ['Lot', vehicle.lot],
  ];

  return (
    <article className={styles.detail}>
      <button type="button" className={styles.back} onClick={onBack}>
        <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
          <path
            d="M10.5 2.5 5 8l5.5 5.5"
            fill="none"
            stroke="currentColor"
            strokeWidth="2"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
        Back to inventory
      </button>

      <header className={styles.header}>
        <div>
          <h1 className={styles.title}>
            {vehicle.year} {vehicle.make} {vehicle.model}{' '}
            <span className={styles.trim}>{vehicle.trim}</span>
          </h1>
          <p className={styles.subtitle}>
            {capitalize(vehicle.body_style)} · Lot {vehicle.lot} · {vehicle.city},{' '}
            {vehicle.province}
          </p>
        </div>
        {wonBuyNow ? (
          <span className={styles.soldChip}>Sold</span>
        ) : (
          <AuctionCountdown timing={timing} now={now} />
        )}
      </header>

      <div className={styles.layout}>
        <div className={styles.content}>
          <section className={styles.gallery} aria-label="Photos">
            <div className={styles.mainImage}>
              <VehicleImage
                key={mainImage ?? 'none'}
                src={mainImage}
                alt={`${alt}, photo ${imageIndex + 1} of ${vehicle.images.length}`}
                fallbackLabel={alt}
                loading="eager"
              />
            </div>
            {vehicle.images.length > 1 && (
              <ul className={styles.thumbs}>
                {vehicle.images.map((image, index) => (
                  <li key={image}>
                    <button
                      type="button"
                      className={`${styles.thumb} ${index === imageIndex ? styles.thumbActive : ''}`}
                      onClick={() => setImageIndex(index)}
                      aria-label={`Show photo ${index + 1}`}
                      aria-current={index === imageIndex}
                    >
                      <VehicleImage key={image} src={image} alt="" />
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </section>

          {vehicle.title_status !== 'clean' && (
            <aside className={styles.titleWarning}>
              <TitleStatusBadge status={vehicle.title_status} size="lg" />
              <p>
                This vehicle carries a {vehicle.title_status} title. Review the condition report
                and damage notes carefully before bidding.
              </p>
            </aside>
          )}

          <section className={styles.card} aria-label="Specifications">
            <h2 className={styles.sectionTitle}>Specifications</h2>
            <dl className={styles.specs}>
              {specs.map(([label, value]) => (
                <div key={label} className={styles.specRow}>
                  <dt className={styles.specLabel}>{label}</dt>
                  <dd className={label === 'VIN' ? styles.specValueMono : styles.specValue}>
                    {value}
                  </dd>
                </div>
              ))}
            </dl>
          </section>

          <section className={styles.card} aria-label="Condition">
            <div className={styles.conditionHeader}>
              <h2 className={styles.sectionTitle}>Condition</h2>
              <ConditionBadge grade={vehicle.condition_grade} size="lg" />
            </div>
            <p className={styles.report}>{vehicle.condition_report}</p>
            {vehicle.damage_notes.length > 0 ? (
              <ul className={styles.damageList}>
                {vehicle.damage_notes.map((note) => (
                  <li key={note} className={styles.damageItem}>
                    <svg viewBox="0 0 16 16" width="14" height="14" aria-hidden="true">
                      <path
                        d="M8 1.5 15 14H1L8 1.5Zm0 4.5v4m0 2v.01"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="1.6"
                        strokeLinecap="round"
                        strokeLinejoin="round"
                      />
                    </svg>
                    {note}
                  </li>
                ))}
              </ul>
            ) : (
              <p className={styles.noDamage}>No damage reported.</p>
            )}
          </section>
        </div>

        <aside className={styles.sidebar}>
          <BidPanel
            vehicle={vehicle}
            now={now}
            isHighBidder={isHighBidder}
            wonBuyNow={wonBuyNow}
            onPlaceBid={onPlaceBid}
            onBuyNow={onBuyNow}
          />

          <section className={styles.card} aria-label="Seller">
            <h2 className={styles.sectionTitle}>Seller</h2>
            <p className={styles.dealership}>{vehicle.selling_dealership}</p>
            <p className={styles.dealershipMeta}>
              {vehicle.city}, {vehicle.province}
            </p>
            <div className={styles.sellerBadges}>
              <TitleStatusBadge status={vehicle.title_status} />
              <ReserveBadge state={reserveState(vehicle)} />
            </div>
          </section>
        </aside>
      </div>
    </article>
  );
}
