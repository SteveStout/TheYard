/**
 * The foot of the sidebar: the site map's actions in its order, each a pinned
 * row, with Reset bids under Admin while there are bids. Its own file because
 * the foot draws from the site map and the views, not from the document
 * sections above it. The two links out arrive as addresses from SideNav.
 */
import { SITE_MAP, type SiteAction } from '../../../lib/siteMap';
import { RailPinnedRow } from '../RailPinnedRow';
import { RailResetBidsRow } from '../RailResetBidsRow';
import styles from '../SideNav/SideNav.module.css';

/** The pinned actions that open a view in the app, rather than a page in a new tab. */
export type RailViewKey = Exclude<SiteAction['key'], 'resume' | 'repo'>;

/** The pinned actions that are links out, opened in a new tab. */
export type RailLinkKey = Extract<SiteAction['key'], 'resume' | 'repo'>;

/** Tells a link-out action from one that opens a view. */
function isLinkOut(key: SiteAction['key']): key is RailLinkKey {
  return key === 'resume' || key === 'repo';
}

/**
 * Every rail action from the site map as a pinned row. A view's row reads as
 * current while that view shows; the account row is labelled with the
 * signed-in address when there is one. Every row closes the drawer first.
 */
export function RailPinnedRows({
  iconsOnly,
  views,
  links,
  accountEmail,
  bidCount,
  onResetBids,
  onCloseDrawer,
}: {
  iconsOnly: boolean;
  views: Record<RailViewKey, { current: boolean; open: () => void }>;
  links: Record<RailLinkKey, string>;
  accountEmail: string | null;
  bidCount: number;
  onResetBids: () => void;
  onCloseDrawer: () => void;
}) {
  return (
    <div className={styles.pinned}>
      {/* #region pinned-rows */}
      {/* The site map's actions, in its order. The account row's label is
          the signed-in address when there is one, which is also how a
          visitor checks who they are without opening anything; .label
          already truncates, so a long address does not widen the rail.
          Reset bids is not in the map, because it is not a place: it
          appears after Admin only while there are bids to reset. */}
      {SITE_MAP.actions
        .filter((action) => action.inRail)
        .map((action) => {
          const key = action.key;
          return (
            <RailPinnedRow
              key={key}
              action={action}
              iconsOnly={iconsOnly}
              current={isLinkOut(key) ? false : views[key].current}
              label={key === 'account' ? (accountEmail ?? action.label) : action.label}
              href={isLinkOut(key) ? links[key] : undefined}
              onOpen={() => {
                onCloseDrawer();
                if (!isLinkOut(key)) views[key].open();
              }}
              after={
                key === 'admin' && bidCount > 0 ? (
                  <RailResetBidsRow
                    bidCount={bidCount}
                    iconsOnly={iconsOnly}
                    onReset={() => {
                      onCloseDrawer();
                      onResetBids();
                    }}
                  />
                ) : null
              }
            />
          );
        })}
      {/* #endregion pinned-rows */}
    </div>
  );
}
