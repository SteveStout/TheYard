/**
 * The cold-start window: the first minutes after the process starts, which the
 * speed tile leaves out. It splits per-minute slots into warm ones and cold
 * ones. It is its own file because the rule and its reason are worth reading
 * on their own, and the documents quote this region live.
 */

// #region cold-start
/**
 * The first few minutes after a process starts are not held against it. A cold
 * process takes about a second to answer its first request, and on a quiet
 * minute that one request is the whole 95th percentile. Counting it would turn
 * the speed tile amber after every deploy, and an alarm that fires on every
 * deploy teaches people to ignore it. So the tile leaves these minutes out and
 * says so on its face. The traffic card still draws them.
 */
export const COLD_START_MINUTES = 3;

/**
 * Splits per-minute slots into warm ones and the ones inside the cold-start
 * window. The window starts at the minute the process started (rounded down).
 * With no start time, every slot counts as warm.
 */
export function afterColdStart<T extends { at: string }>(
  slots: T[],
  startedAt: Date | null
): { warm: T[]; left_out: T[] } {
  if (startedAt === null || Number.isNaN(startedAt.getTime())) return { warm: slots, left_out: [] };
  const from = Math.floor(startedAt.getTime() / 60_000) * 60_000;
  const until = from + COLD_START_MINUTES * 60_000;
  const cold = (slot: T) => {
    const at = new Date(slot.at).getTime();
    return at >= from && at < until;
  };
  return { warm: slots.filter((slot) => !cold(slot)), left_out: slots.filter(cold) };
}
// #endregion cold-start
