import { useState } from 'react';
import type { Vehicle } from '../lib/types';
import { auctionTiming, currentPrice, reserveState } from '../lib/auction';
import type { BidOutcome } from '../lib/data';
import { formatAuctionDateTime, formatCountdown, formatCurrency } from '../lib/format';
import { AuctionCountdown } from './AuctionCountdown';
import { Readout } from './Readout';
import { ReserveBadge } from './ReserveBadge';
import { Ring } from './Ring';
import styles from './BidPanel.module.css';

interface BidPanelProps {
  /** The vehicle with the buyer's own bids already merged in. */
  vehicle: Vehicle;
  now: number;
  isHighBidder: boolean;
  /** The simulated room has bid past the buyer here (ADR-027). */
  isOutbid: boolean;
  wonBuyNow: boolean;
  /** Whether anybody is signed in: a bid belongs to an account (ADR-037). */
  signedIn: boolean;
  /** Where the site signs a visitor in; the panel sends a signed-out one there. */
  onOpenAccount: () => void;
  /** Bids are validated by the API; these resolve to its verdict. */
  onPlaceBid: (amount: number) => Promise<BidOutcome>;
  onBuyNow: () => Promise<BidOutcome>;
}

export function BidPanel({
  vehicle,
  now,
  isHighBidder,
  isOutbid,
  wonBuyNow,
  signedIn,
  onOpenAccount,
  onPlaceBid,
  onBuyNow,
}: BidPanelProps) {
  const [amountInput, setAmountInput] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const timing = auctionTiming(vehicle, now);
  // A buy-now purchase ends the auction immediately, whatever the clock says,
  // and it ends it for everybody: the server says `sold` on the vehicle and
  // refuses every bid on it, so the panel offers none (ADR: Accounts and
  // per-user bids, the addendum on the second buyer).
  const sold = wonBuyNow || vehicle.sold;
  const status = sold ? 'ended' : timing.status;
  const hasBids = vehicle.current_bid !== null;
  const min = vehicle.min_next_bid;
  // #region stale-minimum
  // The minimum next bid is domain math (tiered increments) and only the
  // server has it, so the browser cannot recompute it when a competing bid
  // arrives. It can tell that the one it holds is out of date: a minimum at or
  // below the standing price is arithmetically impossible. For the moment
  // between the room raising a bid and the refetch landing, the panel says so
  // rather than showing a number that would be rejected on submission.
  const minIsStale = status === 'live' && min <= currentPrice(vehicle);
  // #endregion stale-minimum
  const reserve = reserveState(vehicle);
  const wonAtClose =
    status === 'ended' && isHighBidder && (reserve === 'met' || reserve === 'no-reserve');
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
    <section className={`${styles.panel} op-glass`} aria-label="Auction">
      <div className={styles.statusRow}>
        {sold ? (
          <span className={styles.soldChip}>Sold</span>
        ) : (
          <AuctionCountdown timing={timing} now={now} />
        )}
        <span className={styles.bidCount}>
          {vehicle.bid_count} {vehicle.bid_count === 1 ? 'bid' : 'bids'}
        </span>
      </div>

      <div className={styles.priceRow}>
        {/* The auction's time as a ring (the operator's look): how much of its
            window is left, its words inside, gold because it is one series of
            its own; full and ended once it is over. */}
        <Ring
          value={status === 'live' ? timing.endsAt - now : status === 'ended' || sold ? 1 : 0}
          max={status === 'live' ? timing.endsAt - timing.startsAt : 1}
          size="page"
          tone="second"
          inside={
            sold
              ? 'Sold'
              : status === 'live'
                ? formatCountdown(timing.endsAt, now)
                : status === 'ended'
                  ? 'Ended'
                  : 'Soon'
          }
          label={
            sold
              ? 'Sold'
              : status === 'live'
                ? `${formatCountdown(timing.endsAt, now)} left of the auction`
                : status === 'ended'
                  ? 'The auction has ended'
                  : `Bidding opens in ${formatCountdown(timing.startsAt, now)}`
          }
          testId="bid-ring"
        />
        <div className={styles.priceBlock}>
          <span className={styles.priceLabel}>
            {sold ? 'Purchase price' : hasBids ? 'Current bid' : 'Starting bid'}
          </span>
          <span className={styles.price}>{formatCurrency(currentPrice(vehicle))}</span>
          <ReserveBadge state={reserve} />
        </div>
      </div>
      <Readout
        rows={[
          ['Bids', String(vehicle.bid_count)],
          [status === 'upcoming' ? 'Opens' : 'Opened', formatAuctionDateTime(timing.startsAt)],
          [
            status === 'live' || status === 'upcoming' ? 'Ends' : 'Ended',
            formatAuctionDateTime(timing.endsAt),
          ],
          ...(status === 'live' && !sold && !minIsStale
            ? [['Minimum next', formatCurrency(min)] as const]
            : []),
        ]}
        testId="bid-readout"
      />

      {wonBuyNow && (
        <p className={styles.wonBox}>
          You bought this vehicle for {formatCurrency(currentPrice(vehicle))}.
        </p>
      )}

      {sold && !wonBuyNow && (
        <p className={styles.endedBox}>
          Someone bought this vehicle for {formatCurrency(currentPrice(vehicle))}.
        </p>
      )}

      {status === 'ended' && !sold && (
        <p className={wonAtClose ? styles.wonBox : styles.endedBox}>
          {wonAtClose
            ? `You won this auction at ${formatCurrency(currentPrice(vehicle))}.`
            : isHighBidder
              ? 'The auction ended below reserve, so the vehicle was not sold.'
              : isOutbid
                ? 'This auction ended with someone else ahead of you.'
                : 'This auction has ended.'}
        </p>
      )}

      {status === 'upcoming' && (
        <p className={styles.upcomingBox}>
          Bidding opens in {formatCountdown(timing.startsAt, now)}.
        </p>
      )}

      {/* #region signed-out */}
      {/* A bid belongs to an account, and the server refuses one from nobody
          with a 401 (ADR-037). Until 1.0.0.139 the panel did not know who was
          looking, so a signed-out visitor typed an amount, pressed the button
          and learned from the server's refusal. Now the panel asks the one
          question the server will ask, and asks it first: signed out, the form
          and the buy-now button are not rendered at all, and the one control
          left says what it needs and goes there, the way the proof card's does
          (ADR: Same performance, proven, addendum). Not a disabled form with an
          instruction on it: on a phone that reads as broken. */}
      {status === 'live' && !signedIn && (
        <button
          type="button"
          className={styles.bidButton}
          onClick={onOpenAccount}
          data-testid="bid-sign-in"
        >
          Sign in to bid
        </button>
      )}
      {/* #endregion signed-out */}

      {status === 'live' && signedIn && (
        <>
          {/* #region outbid */}
          {/* The two halves of the same sentence (ADR-027). role="status" on
              both, so a screen reader is told when the lead changes hands
              rather than only seeing it. */}
          {isHighBidder && (
            <p className={styles.highBidder} role="status">
              You're the high bidder at {formatCurrency(currentPrice(vehicle))}
            </p>
          )}

          {isOutbid && (
            <p className={styles.outbid} role="status" data-testid="outbid-notice">
              Someone outbid you. The bid stands at {formatCurrency(currentPrice(vehicle))}.
            </p>
          )}
          {/* #endregion outbid */}

          <form className={styles.form} onSubmit={submitBid}>
            <label className={styles.inputLabel} htmlFor="bid-amount">
              Your bid{' '}
              <span className={styles.minHint}>
                {minIsStale ? '(updating the minimum)' : `(minimum ${formatCurrency(min)})`}
              </span>
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
                  placeholder={minIsStale ? '' : String(min)}
                  value={amountInput}
                  onChange={(e) => {
                    setAmountInput(e.target.value);
                    setError(null);
                  }}
                />
              </div>
              <button type="submit" className={styles.bidButton} disabled={pending || minIsStale}>
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
