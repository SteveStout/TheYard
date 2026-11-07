/**
 * The shape of the cost answer the server sends for the Admin tab's cost card:
 * the windows, the days, the forecast, and the split by resource and by type.
 * Only types live here, so the words and every picture can name the same shape
 * without importing each other.
 */

// #region spend-types
/** The stretch a cost answer covers: the last day, week or month. */
export type CostWindow = '24h' | '7d' | '30d';

/** One day on the spend line: what it cost, the running total, and whether Azure is still adding to it. */
export type CostPoint = { day: string; cost: number; total: number; partial: boolean };

/** One resource's slice of the donut: its cost over the window, a month at that rate, and its share. */
export type CostSlice = {
  name: string;
  type: string;
  cost: number;
  monthly: number;
  share: number;
  count: number;
};

/** One kind of resource on the bill: how many there are, and what they cost over the window and a month. */
export type CostKind = {
  type: string;
  label: string;
  resources: number;
  cost: number;
  monthly: number;
};

/** One day as the spike rule reads it, beside the median of the finished week before it (DaySpike.cs). */
export type SpikeDay = {
  day: string;
  cost: number;
  partial: boolean;
  /** The median of up to seven finished days before it; null when none is kept. */
  median: number | null;
  /** How many finished days that median is taken over. */
  days: number;
  /** The day's cost over that median, to one place. */
  multiple: number | null;
  /** The resource with the biggest line that day, and what it cost. */
  biggest: string | null;
  biggest_cost: number;
};

/** One meter that can run away and the rule that watches it (MeterRows.cs). */
export type MeterRow = {
  meter: string;
  watches: string;
  rule: string;
  /** exists, off, not set or not read yet. */
  state: string;
  line: string;
};

/** GET /api/admin/costs, as the server writes it (api/TheYard.Api/Shared/CostReadings/CostReport.cs). */
export type CostReport = {
  window: CostWindow;
  windows: CostWindow[];
  available: boolean;
  note: string | null;
  currency: string;
  read_at_ms: number | null;
  newest_day: string | null;
  newest_partial: boolean;
  month: string | null;
  month_to_date: number;
  /** The month to date plus Azure's forecast for each day after the newest reported: never below the month to date. */
  forecast_month: number | null;
  /** Azure's forecast answer added up as the portal shows it; its billed days lag the bill, so it can read below the month to date. */
  azure_forecast_month?: number | null;
  /** The month if every day left costs what the last finished day did. */
  month_at_yesterday?: number | null;
  /** The last finished day that rate is taken from, yyyy-MM-dd. */
  yesterday_day?: string | null;
  /** What that day cost. */
  yesterday_cost?: number | null;
  /** The judged day and the seven before it, newest first; optional so an older answer draws "not read yet". */
  spike_days?: SpikeDay[];
  /** Every meter that can run away and its rule; optional for the same reason. */
  meters?: MeterRow[];
  window_total: number;
  rate_days: number;
  month_days: number;
  days: CostPoint[];
  forecast: CostPoint[];
  resources: CostSlice[];
  types: CostKind[];
};
// #endregion spend-types
