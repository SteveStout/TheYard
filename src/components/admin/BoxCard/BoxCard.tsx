/**
 * WickedAwsome, the box that supports the job hunt (ADR: The box reports itself): the card the server draws in the
 * shared frame, read from GET /api/admin/cloud/box for the tab's window, with the four live panels handed to the
 * frame between its lead block and its chart.
 */
import type { ReactNode } from 'react';
import type { CloudReport } from '../../../lib/cloud';
import type { MachineWindow } from '../../../lib/machineChart';
import { useRead } from '../shared/common';
import { CardShell } from '../CardShell';
import { planWindow } from '../PlanCard/PlanCard';
import { BoxLivePanels } from './BoxLivePanels';

export default function BoxCard({
  title,
  tick,
  window,
  toolbar,
}: {
  title: string;
  tick: number;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  const report = useRead<CloudReport>(`/api/admin/cloud/box?window=${planWindow(window)}`, tick);
  return (
    <CardShell
      report={report}
      title={title}
      testId="box-card"
      toolbar={toolbar}
      live={<BoxLivePanels />}
    />
  );
}
