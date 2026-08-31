/**
 * A vehicle listing exactly as it appears in data/vehicles.json — the shape a
 * real inventory API would return. Field names stay snake_case to match the
 * payload, so the seam in data.ts needs no mapping layer.
 */
export interface Vehicle {
  id: string;
  vin: string;
  year: number;
  make: string;
  model: string;
  trim: string;
  body_style: string;
  exterior_color: string;
  interior_color: string;
  engine: string;
  transmission: string;
  drivetrain: string;
  odometer_km: number;
  fuel_type: string;
  /** 0–5, one decimal. */
  condition_grade: number;
  condition_report: string;
  /** May be empty for vehicles with no reported damage. */
  damage_notes: string[];
  /** "clean" | "rebuilt" | "salvage" in the current dataset; kept open. */
  title_status: string;
  province: string;
  city: string;
  /** Synthetic scheduling data; real auction windows are derived in auction.ts. */
  auction_start: string;
  starting_bid: number;
  /** null → no reserve. Never displayed — the UI shows only the reserve state. */
  reserve_price: number | null;
  /** null → no Buy Now option. */
  buy_now_price: number | null;
  images: string[];
  selling_dealership: string;
  lot: string;
  /** null until the first bid is placed (bid_count === 0). */
  current_bid: number | null;
  bid_count: number;

  // --- Server-derived auction facts, appended by the API (VehicleWire) ---
  /** Auction window as epoch ms; the browser only formats and counts down. */
  auction_starts_at: number;
  auction_ends_at: number;
  /** Status at the moment the server answered; recompute from the window as time passes. */
  auction_status: 'upcoming' | 'live' | 'ended';
  /** The minimum acceptable bid right now, per the server's rules. */
  min_next_bid: number;
}
