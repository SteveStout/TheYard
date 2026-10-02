/**
 * What the machines are doing (ADR: What the machines are doing), over the window
 * every chart on the tab shares, which the tab hands in. This file is the card's
 * frame and the list of its parts, each in the folder beside it:
 *   MachinesKeptWindow         a day, a week or a month, from the kept minutes
 *   MachinesContainerSection   the container: memory, processor, catalogues
 *   MachinesRelationalSection  the relational store's own resource view
 *   MachinesDocumentSection    the document store's request units
 */
import type { ReactNode } from 'react';
import { type MachineWindow, windowName } from '../../../lib/machineChart';
import styles from '../shared/card.module.css';
import type { Fetched, Machines } from '../shared/types';
import { About } from '../shared/common';
import { MachinesKeptWindow } from '../MachinesKeptWindow';
import { MachinesContainerSection } from '../MachinesContainerSection';
import { MachinesRelationalSection } from '../MachinesRelationalSection';
import { MachinesDocumentSection } from '../MachinesDocumentSection';

/**
 * The machines card: a loading or failed line until the answer arrives, then
 * the whole card. The toolbar is the tab's shared window buttons, drawn here
 * in every state so the window can change while the card waits.
 */
export default function MachinesCard({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Fetched<Machines>;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  if (machines === null || machines === 'failed') {
    return (
      <article className={`${styles.wide} op-glass`} data-testid="machines-card">
        <h2 className={styles.cardTitle}>What the machines are doing</h2>
        {toolbar}
        {machines === null ? (
          <p className={styles.muted}>Loading…</p>
        ) : (
          <p className={styles.muted} data-testid="machines-failed">
            Could not read the machines on the last try.
          </p>
        )}
      </article>
    );
  }
  return <MachinesBody machines={machines} window={window_} toolbar={toolbar} />;
}

/**
 * The card once the machines have answered: what it shows and why, the window
 * line, the kept window when one wider than the hour is chosen, then the
 * three machines in turn.
 */
function MachinesBody({
  machines,
  window: window_,
  toolbar,
}: {
  machines: Machines;
  window: MachineWindow;
  toolbar: ReactNode;
}) {
  return (
    <article className={`${styles.wide} op-glass`} data-testid="machines-card">
      <h2 className={styles.cardTitle}>What the machines are doing</h2>
      <About>
        The three machines under this site, each reporting the way it actually reports. The
        container knows its own memory and its own processor time, and the limit its share is read
        against is the runtime&rsquo;s own, which is lower than the memory the machine has, and a
        process that passes its own limit is the one that gets collected; Azure SQL Database keeps a
        reading of itself for the last hour, fifteen seconds at a time, free on every tier; Azure
        Cosmos DB has no memory or processor reading to give, because it is sold by request unit, so
        what it shows is what the operations cost against the free allowance. The last hour is this
        container&rsquo;s own, kept in its memory, and empties on every roll. The wider windows are
        a minute at a time, kept in Azure Cosmos DB by each site for thirty-one days, so they
        survive a roll and show one.
      </About>
      {toolbar}
      <p className={styles.muted} data-testid="machines-window-line">
        Showing {windowName(window_).toLowerCase()}. One window for every chart on this tab: the
        buttons here, on the traffic card and over the tiles are the same buttons.
      </p>
      {window_ !== '1h' && <MachinesKeptWindow machines={machines} window={window_} />}
      <MachinesContainerSection container={machines.container} window={window_} />
      <MachinesRelationalSection relational={machines.relational} />
      <MachinesDocumentSection documentStore={machines.document} />
    </article>
  );
}
