// The entry point for the site. It reads like a table of contents: each line starts one part, in
// order, and the comment beside it names the file that holds the details. Program.cs does the
// same for the API.
import './styles/globalStyles';
import { mountTheYard } from './app/mount';
import { reportUncaughtErrors } from './app/reportUncaughtErrors';
import { captureAdminKey } from './lib/adminKey';

// #region bootstrap
// styles/globalStyles.ts: the site-wide stylesheets, loaded by the import above, in order
captureAdminKey(); // lib/adminKey.ts: keep the operator's key before the address bar is tidied
reportUncaughtErrors(); // app/reportUncaughtErrors.ts: report crashes an error boundary never sees
mountTheYard('root'); // app/mount.tsx: draw the site into <div id="root"> in index.html
// #endregion bootstrap
