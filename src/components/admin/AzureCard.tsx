/**
 * Azure's view of the container (ADR-010), asked by the container with its own identity.
 */
import { shortenDigests } from '../../lib/format';
import styles from '../AdminPanel.module.css';
import type { AzureState } from './types';
import { useRead, pill, failed } from './common';

export default function AzureCard({ tick }: { tick: number }) {
  const azure = useRead<AzureState>('/api/admin/azure', tick);
  return (
    <article className={`${styles.card} op-glass`} data-testid="azure-card">
      <h2 className={styles.cardTitle}>Azure's view of the container</h2>
      {azure === null ? (
        <p className={styles.muted}>Loading…</p>
      ) : azure === 'failed' ? (
        failed("Azure's view")
      ) : azure.available && azure.host === 'app-service' ? (
        // A web app on a shared plan reports a different set of facts from a
        // container group, and the card shows the set it has: the site's
        // state, the image it was told to run, and the plan both sites share
        // (ADR: One plan, two sites).
        <>
          <ul className={styles.checkList}>
            <li className={styles.checkRow}>
              <span className={pill(azure.group_state === 'Running')}>{azure.group_state}</span>
              <span>
                web app{azure.region ? `, ${azure.region}` : ''}, availability{' '}
                {azure.availability?.toLowerCase()}
              </span>
            </li>
            <li className={styles.checkRow}>
              <span className={styles.mono}>{azure.plan_name ?? 'plan unread'}</span>
              <span className={styles.muted}>
                {azure.plan_sku ?? 'size unread'}
                {typeof azure.plan_sites === 'number'
                  ? `, ${azure.plan_sites} site${azure.plan_sites === 1 ? '' : 's'} sharing it`
                  : ''}
              </span>
            </li>
            <li className={styles.checkRow}>
              <span className={styles.mono}>{azure.image?.split('/').pop()}</span>
              <span className={styles.muted}>image Azure reports</span>
            </li>
            <li className={styles.checkRow}>
              <span className={pill(azure.always_on === true)}>
                {azure.always_on ? 'Always On' : 'Always On is off'}
              </span>
              <span className={styles.muted}>
                {azure.health_check_path
                  ? `the platform asks ${azure.health_check_path} and replaces an instance that stops answering`
                  : 'no health check path is set'}
              </span>
            </li>
          </ul>
          <p className={styles.muted}>
            App Service keeps no restart count and no container events where this site's identity
            can read them, so neither is shown. Uptime on the health card is the restart story here.
          </p>
        </>
      ) : azure.available ? (
        <ul className={styles.checkList}>
          <li className={styles.checkRow}>
            <span className={pill(azure.group_state === 'Running')}>{azure.group_state}</span>
            <span>container group</span>
          </li>
          <li className={styles.checkRow}>
            <span className={pill(azure.container_state === 'Running')}>
              {azure.container_state}
            </span>
            <span>
              container, {azure.restart_count} restart{azure.restart_count === 1 ? '' : 's'}
            </span>
          </li>
          <li className={styles.checkRow}>
            <span className={styles.mono}>{azure.image?.split('/').pop()}</span>
            <span className={styles.muted}>image Azure reports</span>
          </li>
          {azure.events && azure.events.length > 0 && (
            <li className={styles.checkRow}>
              <span className={styles.muted}>recent events, newest first</span>
            </li>
          )}
          {azure.events?.map((event, index) => (
            <li key={index} className={styles.checkRow} data-testid="azure-event">
              <span className={styles.mono}>{event.name}</span>
              <span className={styles.muted}>
                {event.count > 1 ? `${event.count} times, last ` : ''}
                {event.last_at ? new Date(event.last_at).toLocaleString() : ''}
              </span>
              <span className={styles.muted}>{shortenDigests(event.message)}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className={styles.muted}>
          The Azure view is unavailable from here ({azure.reason}). It works when this page is
          served by the container on Azure, which asks about itself with its own identity.
        </p>
      )}
    </article>
  );
}
