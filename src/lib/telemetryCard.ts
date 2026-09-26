/**
 * What the telemetry card says when Application Insights holds no request for
 * the hour (1.0.3.31; ADR: The tweaks pass, the addendum of 1.0.3.31). On 25
 * September the card said "0 requests" while the strip above it counted
 * hundreds: the component had taken 20,879 requests between 12:00 and 18:00
 * UTC against a daily data cap of 0.1 GB, and a component at its cap takes
 * nothing more until midnight UTC. Zeros
 * drawn as a reading say the site was quiet; a sentence says what is true.
 */

/** A sentence for an hour Application Insights holds nothing for, or null when it holds requests. */
export function emptyHourWords(
  total: number,
  newestAt: string | null | undefined,
  timeOf: (iso: string) => string
): string | null {
  if (total > 0) return null;
  const newest =
    newestAt !== null && newestAt !== undefined && newestAt !== '' && timeOf(newestAt) !== ''
      ? `Its newest request is from ${timeOf(newestAt)}.`
      : 'It holds none from the last day either.';
  return (
    `Application Insights holds no request from the last hour. ${newest} ` +
    'When the traffic card counts requests over the same hour, the likeliest reason is ' +
    'the component’s daily data cap, which stops it taking data until midnight UTC.'
  );
}
