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

/** GET /api/admin/costs, as the server writes it (api/TheYard.Api/CostReport.cs). */
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
  forecast_month: number | null;
  window_total: number;
  rate_days: number;
  month_days: number;
  days: CostPoint[];
  forecast: CostPoint[];
  resources: CostSlice[];
  types: CostKind[];
};
// #endregion spend-types
