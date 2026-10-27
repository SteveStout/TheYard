/**
 * Azure's view of the container (ADR-010), asked by the container with its own identity, drawn in the shape
 * every card under Cloud infrastructure shares (ADR: One plan, two sites, the addendum on the shell's shape):
 * the site's state, its plan, its image and Always On in the lead, and the hourly readings, the certificate
 * and the storage, in a readout beside them. The wire gives one figure with a ceiling, so there are no dials.
 */
import { CardState } from '../shared/CardState';
import { shortenDigests, formatDateTime } from '../../../lib/format';
import styles from '../shared/card.module.css';
import areas from '../shared/cardAreas.module.css';
import type { AzureState } from '../shared/types';
import { useRead, pill, failed, tonePill } from '../shared/common';
import { Range } from '../shared/Range';
import { toneOf } from '../../../lib/ranges';

/** The certificate's days left and the relational store's size, each judged against its range. */
function HourlyLines({ hourly }: { hourly: NonNullable<AzureState['hourly']> | null }) {
  if (hourly === null) {
    return (
      <p className={styles.muted} data-testid="azure-hourly">
        The certificate and the storage are read once an hour; the first reading has not been taken.
      </p>
    );
  }
  const certificate = hourly.certificate;
  const storage = hourly.sql_storage;
  return (
    <ul className={styles.checkList} data-testid="azure-hourly">
      <li className={styles.checkRow} data-testid="azure-certificate">
        {certificate === null ? (
          <span className={styles.muted}>
            No certificate read here: this container has no public address of its own.
          </span>
        ) : (
          <>
            <span className={tonePill(toneOf('certificateDays', certificate.days_left))}>
              {certificate.days_left} days
            </span>
            <span className={styles.muted}>
              left on the certificate for {certificate.host}, to{' '}
              {formatDateTime(certificate.not_after)}
            </span>
          </>
        )}
      </li>
      <li className={styles.checkRow} data-testid="azure-storage">
        {storage === null ? (
          <span className={styles.muted}>
            No storage read here: there is no Azure SQL Database behind this container.
          </span>
        ) : (
          <>
            <span className={tonePill(toneOf('sqlStorage', storage.share))}>
              {Math.round(storage.share * 100)}%
            </span>
            <span className={styles.muted}>
              of the relational store's {storage.max_mb.toLocaleString('en-US')} MB,{' '}
              {storage.used_mb.toLocaleString('en-US')} MB in its data files
            </span>
          </>
        )}
      </li>
      <li className={styles.checkRow}>
        <Range of="certificateDays" />
      </li>
      <li className={styles.checkRow}>
        <Range of="sqlStorage" />
      </li>
    </ul>
  );
}

export default function AzureCard({ tick }: { tick: number }) {
  const azure = useRead<AzureState>('/api/admin/azure', tick);
  const read = azure !== null && azure !== 'failed';
  return (
    <article className={`${areas.card} op-glass`} data-testid="azure-card">
      <div className={areas.lead} data-testid="cloud-lead">
        <h2 className={styles.cardTitle}>Azure's view of the container</h2>
        {azure === null ? (
          <CardState state="loading" />
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
                <span className={styles.muted}>the container image Azure reports</span>
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
              can read them, so neither is shown. Uptime on the health card is the restart story
              here.
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
              <span className={styles.muted}>the container image Azure reports</span>
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
                  {event.last_at ? formatDateTime(event.last_at) : ''}
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
      </div>
      {read && (
        <div className={areas.readout}>
          <aside
            className={`${areas.aside} op-glass`}
            data-testid="cloud-readout"
            aria-label="The hourly readings"
          >
            <h3 className={areas.asideHeading}>Read once an hour</h3>
            <HourlyLines hourly={azure.hourly ?? null} />
            {azure.fetched_at !== undefined && (
              <p className={styles.muted} data-testid="azure-fetched">
                Azure answered at {formatDateTime(azure.fetched_at)}; the answer is kept for a
                minute.
              </p>
            )}
          </aside>
        </div>
      )}
    </article>
  );
}
