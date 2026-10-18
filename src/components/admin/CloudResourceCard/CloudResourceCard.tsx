/**
 * Any card under Cloud infrastructure past the plan (ADR: One frame for every resource): it reads
 * GET /api/admin/cloud/{key} for the tab's window and draws the answer in the shared frame. The
 * six resources differ only in their key and title; what each shows is decided on the server.
 */
import type { ReactNode } from 'react';
import type { CloudReport } from '../../../lib/cloud';
import type { MachineWindow } from '../../../lib/machineChart';
import { useRead } from '../shared/common';
import { CloudCard } from '../CloudCard';
import { planWindow } from '../PlanCard/PlanCard';

export default function CloudResourceCard({
  resource,
  title,
  tick,
  window,
  toolbar,
}: {
  resource: string;
  title: string;
  tick: number;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const report = useRead<CloudReport>(
    `/api/admin/cloud/${resource}?window=${planWindow(window)}`,
    tick
  );
  return <CloudCard report={report} title={title} testId={`${resource}-card`} toolbar={toolbar} />;
}
