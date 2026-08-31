import { useState } from 'react';
import type { Vehicle } from '../lib/types';
import { auctionTiming, currentPrice, reserveState } from '../lib/auction';
import type { BidOutcome } from '../lib/data';
import { formatCountdown, formatCurrency } from '../lib/format';
import { AuctionCountdown } from './AuctionCountdown';
import { ReserveBadge } from './ReserveBadge';
import styles from './BidPanel.module.css';

interface BidPanelProps {
  /** The vehicle with the buyer's own bids already merged in. */
  vehicle: Vehicle;
  now: number;
  isHighBidder: boolean;
  wonBuyNow: boolean;
  /** Bids are validated by the API; these resolve to its verdict. */
  onPlaceBid: (amount: number) => Promise<BidOutcome>;
  onBuyNow: () => Promise<BidOutcome>;
}

export function BidPanel({ vehicle, now, isHighBidder, wonBuyNow, onPlaceBid, onBuyNow }: BidPanelProps) {
  const [amountInput, setAmountInput] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const timing = auctionTiming(vehicle, now);
  // A buy-now purchase ends the auction immediately, whatever the clock says.
  const status = wonBuyNow ? 'ended' : timing.status;
  const hasBids = vehicle.current_bid !== null;
  const min = vehicle.min_next_bid;
  const reserve = reserveState(vehicle);
  const wonAtClose = status === 'ended' && isHighBidder && (reserve === 'met' || reserve === 'no-reserve');
  const canBuyNow = status === 'live' && vehicle.buy_now_price !== null;

  const submitBid = async (event: { preventDefault(): void }) => {
    event.preventDefault();
    if (pending) return;
    const amount = Number(amountInput);
    if (!Number.isFinite(amount)) {
      setError('Enter a valid bid amount.');
      return;
    }
    setPending(true);
    const outcome = await onPlaceBid(amount);
    setPending(false);
    if (outcome.kind === 'rejected') {
      setError(outcome.reason);
    } else {
      // The panel re-renders into its high-bidder or "won" state.
      setError(null);
      setAmountInput('');
    }
  };

  const handleBuyNow = async () => {
    if (pending) return;
    setPending(true);
    const outcome = await onBuyNow();
    setPending(false);
    setError(outcome.kind === 'rejected' ? outcome.reason : null);
  };

  return (
    <section className={styles.panel} aria-label="Auction">
      <div className={styles.statusRow}>
        {wonBuyNow ? (
          <span className={styles.soldChip}>Sold</span>
        ) : (
          <AuctionCountdown timing={timing} now={now} />
        )}
        <span className={styles.bidCount}>
          {vehicle.bid_count} {vehicle.bid_count === 1 ? 'bid' : 'bids'}
        </span>
      </div>

      <div className={styles.priceBlock}>
        <span className={styles.priceLabel}>
          {wonBuyNow ? 'Purchase price' : hasBids ? 'Current bid' : 'Starting bid'}
        </span>
        <span className={styles.price}>{formatCurrency(currentPrice(vehicle))}</span>
        <ReserveBadge state={reserve} />
      </div>

      {wonBuyNow && (
        <p className={styles.wonBox}>
          You bought this vehicle for {formatCurrency(currentPrice(vehicle))}.
        </p>
      )}

      {status === 'ended' && !wonBuyNow && (
        <p className={wonAtClose ? styles.wonBox : styles.endedBox}>
          {wonAtClose
            ? `You won this auction at ${formatCurrency(currentPrice(vehicle))}.`
            : isHighBidder
              ? 'The auction ended below reserve — the vehicle was not sold.'
              : 'This auction has ended.'}
        </p>
      )}

      {status === 'upcoming' && (
        <p className={styles.upcomingBox}>
          Bidding opens in {formatCountdown(timing.startsAt, now)}.
        </p>
      )}

      {status === 'live' && (
        <>
          {isHighBidder && (
            <p className={styles.highBidder} role="status">
              You're the high bidder at {formatCurrency(currentPrice(vehicle))}
            </p>
          )}

          <form className={styles.form} onSubmit={submitBid}>
            <label className={styles.inputLabel} htmlFor="bid-amount">
              Your bid <span className={styles.minHint}>(minimum {formatCurrency(min)})</span>
            </label>
            <div className={styles.inputRow}>
              <div className={styles.amountWrap}>
                <span className={styles.currencySign} aria-hidden="true">
                  $
                </span>
                <input
                  id="bid-amount"
                  className={styles.amountInput}
                  type="number"
                  inputMode="numeric"
                  placeholder={String(min)}
                  value={amountInput}
                  onChange={(e) => {
                    setAmountInput(e.target.value);
                    setError(null);
                  }}
                />
              </div>
              <button type="submit" className={styles.bidButton} disabled={pending}>
                {pending ? 'Placing…' : 'Place bid'}
              </button>
            </div>
          </form>

          {error && (
            <p className={styles.error} role="status">
              {error}
            </p>
          )}

          {canBuyNow && vehicle.buy_now_price !== null && (
            <div className={styles.buyNow}>
              <span className={styles.buyNowDivider}>or</span>
              <button
                type="button"
                className={styles.buyNowButton}
                onClick={handleBuyNow}
                disabled={pending}
              >
                Buy now for {formatCurrency(vehicle.buy_now_price)}
              </button>
            </div>
          )}
        </>
      )}
    </section>
  );
}
