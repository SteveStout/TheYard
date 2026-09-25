/**
 * The desk breakpoint the layout turns on (ADR-013): a docked rail, the vehicle's
 * two columns, the header's countdown. One query, read by every component that
 * asks, so a width between two hand-written queries (1023.5 under zoom) cannot
 * fall into neither. The CSS side is the same 1024 (min-width) and its
 * complement (max-width: 1023.98px).
 */
export const DESK = '(min-width: 1024px)';
