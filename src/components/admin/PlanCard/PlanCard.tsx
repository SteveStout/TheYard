/**
 * The App Service plan (ADR: The App Service plan, read every minute), the first card under Cloud
 * infrastructure: it reads GET /api/admin/plan for the tab's window and draws the answer in the
 * frame every resource shares (ADR: One frame for every resource). Everything it shows comes from
 * the kept minutes; this file only chooses the route.
 */
import type { ReactNode } from 'react';
import type { CloudReport } from '../../../lib/cloud';
import type { MachineWindow } from '../../../lib/machineChart';
import { useRead } from '../shared/common';
import { CardShell } from '../CardShell';

/** The tab's hour is read as the day here, because the plan keeps minutes, not seconds. */
export function planWindow(window: MachineWindow): '24h' | '7d' | '30d' {
  return window === '7d' || window === '30d' ? window : '24h';
}

export default function PlanCard({
  tick,
  window,
  toolbar,
}: {
  tick: number;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const report = useRead<CloudReport>(`/api/admin/plan?window=${planWindow(window)}`, tick);
  return (
    <CardShell report={report} title="The App Service plan" testId="plan-card" toolbar={toolbar} />
  );
}
