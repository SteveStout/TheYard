import { useCallback, useRef, useState } from 'react';
import { fetchEnforced, type Enforced } from '../lib/enforced';

/*
 * Does:      Holds the Decision Records index's marks for the visit, read the first time the section opens.
 * Does not:  Read before it is asked, or draw them (RailDocRow.tsx and RailHeldKey.tsx do).
 * Used by:   SideNav.tsx.
 */

/**
 * The Decision Records index's marks (ADR-029, the addendum on the marks), read
 * the first time the section is opened and kept for the visit. A read that
 * fails is asked again at the next open, so a reader who opens the index
 * while the API is waking up still gets the marks once it answers.
 */
export function useRecordMarks(): { enforced: Enforced | null; read: () => void } {
  const [enforced, setEnforced] = useState<Enforced | null>(null);
  const asked = useRef(false);

  const read = useCallback(() => {
    if (asked.current) return;
    asked.current = true;
    void fetchEnforced().then((marks) => {
      if (marks) setEnforced(marks);
      else asked.current = false;
    });
  }, []);

  return { enforced, read };
}
