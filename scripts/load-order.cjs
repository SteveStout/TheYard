// The order a cold first visit loads in, for the Web overview (docs/WEB-OVERVIEW.md). Read only.
//
// The repository's own Playwright Chromium, headless, a fresh context per round so nothing is
// cached: every request in the order it was sent, with when it was sent, when its first byte
// and its last arrived relative to the navigation start, what asked for it (the DevTools
// initiator), its priority, its bytes on the wire, and the paint marks between them. The
// timings come from the DevTools protocol rather than from the page, because the page cannot
// see a request's priority or what asked for it.
//
// CommonJS on purpose: package.json says "type": "module", and this file is run by node from
// anywhere with a log path outside the checkout, so it carries its own extension.
//
// Usage: node scripts/load-order.cjs <log path> <label> <site url> [rounds]
const { chromium } = require('@playwright/test');
const fs = require('fs');
const [log, label, site, roundsArg] = process.argv.slice(2);
const rounds = Number(roundsArg || 3);
function L(m) {
  fs.appendFileSync(log, m + '\n');
  console.log(m);
}
(async () => {
  L(`### load-order ${label} ${site}, ${rounds} rounds, started ${new Date().toISOString()}`);
  const browser = await chromium.launch({ headless: true });
  for (let i = 1; i <= rounds; i++) {
    const context = await browser.newContext({ viewport: { width: 1366, height: 900 } });
    const page = await context.newPage();
    await page.addInitScript(() => {
      window.__lcp = 0;
      try {
        new PerformanceObserver((l) => {
          for (const e of l.getEntries()) window.__lcp = e.startTime;
        }).observe({ type: 'largest-contentful-paint', buffered: true });
      } catch {}
    });
    const client = await context.newCDPSession(page);
    await client.send('Network.enable');
    const reqs = new Map();
    let t0 = null;
    client.on('Network.requestWillBeSent', (e) => {
      if (t0 === null) t0 = e.timestamp;
      const ini = e.initiator || {};
      let by = ini.type || '';
      if (ini.url) by += ' ' + ini.url.replace(site, '');
      else if (ini.stack && ini.stack.callFrames && ini.stack.callFrames[0])
        by += ' ' + ini.stack.callFrames[0].url.replace(site, '');
      reqs.set(e.requestId, {
        url: e.request.url.replace(site, ''),
        type: e.type,
        prio: e.request.initialPriority,
        sent: e.timestamp,
        by,
      });
    });
    client.on('Network.responseReceived', (e) => {
      const r = reqs.get(e.requestId);
      if (r) {
        r.status = e.response.status;
        r.first = e.timestamp;
        r.proto = e.response.protocol;
      }
    });
    client.on('Network.loadingFinished', (e) => {
      const r = reqs.get(e.requestId);
      if (r) {
        r.done = e.timestamp;
        r.bytes = e.encodedDataLength;
      }
    });
    await page.goto(site + '/?nocache=' + Date.now(), { waitUntil: 'load', timeout: 60000 });
    await page.waitForTimeout(4000);
    const marks = await page.evaluate(() => {
      const nav = performance.getEntriesByType('navigation')[0];
      const paints = Object.fromEntries(
        performance.getEntriesByType('paint').map((p) => [p.name, Math.round(p.startTime)])
      );
      return {
        ttfb: Math.round(nav.responseStart),
        dcl: Math.round(nav.domContentLoadedEventEnd),
        load: Math.round(nav.loadEventEnd),
        fp: paints['first-paint'],
        fcp: paints['first-contentful-paint'],
        lcp: Math.round(window.__lcp),
      };
    });
    L(
      `  round ${i}: ttfb ${marks.ttfb} ms, first paint ${marks.fp} ms, FCP ${marks.fcp} ms, DOMContentLoaded ${marks.dcl} ms, load ${marks.load} ms, LCP ${marks.lcp} ms; ${reqs.size} requests`
    );
    let n = 0;
    for (const r of [...reqs.values()].sort((a, b) => a.sent - b.sent)) {
      n++;
      const s = Math.round((r.sent - t0) * 1000),
        f = r.first ? Math.round((r.first - t0) * 1000) : -1,
        d = r.done ? Math.round((r.done - t0) * 1000) : -1;
      L(
        `    ${String(n).padStart(2)} sent ${String(s).padStart(5)} ms, first byte ${String(f).padStart(5)}, done ${String(d).padStart(5)} | ${(r.type || '').padEnd(10)} ${(r.prio || '').padEnd(8)} ${String(r.bytes || 0).padStart(7)} B ${r.status || ''} ${r.proto || ''} | ${r.url.replace(/nocache=\d+/, 'nocache=N')} | by ${r.by.replace(/nocache=\d+/, 'nocache=N')}`
      );
    }
    await context.close();
  }
  await browser.close();
  L(`### load-order ${label} done ${new Date().toISOString()}`);
})().catch((e) => {
  L('FAILED: ' + ((e && e.stack) || e));
  process.exit(1);
});
