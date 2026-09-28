/**
 * Does:      Draws the version line at the foot of every page: the running build and its commit, linked to GitHub.
 * Does not:  Ask which build is running (useRunningBuild.ts does).
 * Used by:   Shell.tsx.
 */
import type { RunningBuild } from './hooks/useRunningBuild';
import styles from './App.module.css';

export function Footer({ build }: { build: RunningBuild }) {
  return (
    <>
      {/* #region footer-version */}
      {/* The running build and its commit, linked to GitHub (ADR-005). */}
      {build && (
        <footer className={styles.footer} data-frame="footer">
          <span data-testid="build-version">
            {build.version === 'dev' ? 'dev build' : `v${build.version}`}
          </span>
          {build.commit !== 'local' && (
            <>
              <span aria-hidden="true">·</span>
              <a
                className={styles.footerCommit}
                href={`https://github.com/SteveStout/TheYard/commit/${build.commit}`}
                target="_blank"
                rel="noreferrer"
              >
                {build.commit}
              </a>
            </>
          )}
        </footer>
      )}
      {/* #endregion footer-version */}
    </>
  );
}
