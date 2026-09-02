/**
 * The lightning bolt beside the name, in the palette's taupe (ADR-016): one
 * drawing for the sidebar's brand block and the phone header (ADR-017).
 */
export function BrandMark({ size, className }: { size: number; className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" width={size} height={size} aria-hidden="true">
      <path d="M13 2 5 14h5l-2 8 8-12h-5l2-8z" fill="currentColor" />
    </svg>
  );
}
