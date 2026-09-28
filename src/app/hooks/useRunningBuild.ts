/**
 * Does:      Asks the running API, once, which build it is: the version and the commit.
 * Does not:  Draw the version line (Footer.tsx and the rail do).
 * Used by:   App.tsx, Shell.tsx, Footer.tsx.
 */
import { useEffect, useState } from 'react';

/** The build the container reports about itself (ADR-005), or null until it answers. */
export type RunningBuild = { version: string; commit: string } | null;

export function useRunningBuild(): RunningBuild {
  /** The running build, reported by the container itself (ADR-005). */
  const [build, setBuild] = useState<RunningBuild>(null);
  // The footer's version line: ask the running API which build it is (ADR-005).
  useEffect(() => {
    fetch('/api/version')
      .then((r) => (r.ok ? r.json() : null))
      .then(setBuild)
      .catch(() => {});
  }, []);
  return build;
}
