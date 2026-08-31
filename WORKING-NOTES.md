# WORKING-NOTES

Written 2026-08-31 at the end of the Monday build day. Next session: Thursday
2026-09-03, the Azure deployment day. Written for me, reading cold after two
interview days.

## What is done

- TheYard is on GitHub with its own history, no fork banner: https://github.com/SteveStout/TheYard
- Decision 8/31, mid-day: KEPT the car auction domain. The equipment rename was
  skipped on purpose; the OPENLANE identity was scrubbed instead (SUBMISSION.md
  deleted, README rewritten as my portfolio project, links repointed, palette
  comments neutralized, visible brand now "The Yard").
- Tests: 81 xUnit + 27 Vitest + 7 Playwright, green locally, CI green on Actions.
- CLAUDE.md committed: architecture rules, bid rules, test gates.
- Multi-stage Dockerfile + .dockerignore, hand-reviewed line by line.
- The API serves the SPA: UseDefaultFiles + UseStaticFiles + MapFallbackToFile("index.html")
  placed after the API routes, so deep links work and /api is never swallowed.
- Image builds, container runs healthy, all five manual checks passed.
- Two-command workflow for visitors: npm run docker / npm run docker:stop.

## The exact commands

- Dev: npm install then npm start (API + frontend together)
- Tests: dotnet test api/TheBlock.slnx ; npm test -- --run ; npx playwright test
- Container: npm run docker  ->  http://localhost:8080 ; npm run docker:stop
- Raw: docker build -t theyard:local .
       docker run --rm -d -p 8080:8080 --name theyard theyard:local

## Image facts

- Tag: theyard:local
- Stages: node:22-alpine (frontend build), dotnet/sdk:10.0 (publish),
  dotnet/aspnet:10.0 (runtime)
- Target framework: net10.0, read out of the csproj at build time (NOT .NET 9;
  the Monday plan had that wrong)
- Runs as the aspnet image's BUILT-IN non-root user "app"; ownership set per-COPY
  with --chown, no chown layer
- Size: about 382 MB on disk, 114 MB content; my layers are roughly 17 MB on top
  of the Microsoft runtime (publish 15.3 MB, docs ~1 MB, data 303 kB, SPA 319 kB)
- HEALTHCHECK: curl http://localhost:8080/api/facets every 30s
- The image carries README.md, docs/ and data/ because the app serves the docs
  from the About menu and loads the dataset at runtime (they resolve by walking
  up from the app directory)

## What broke today and how it was fixed

1. Fresh machine: az, Docker, WSL, .NET 9 SDK all absent. winget installs plus
   one reboot for WSL2. Machine is fully provisioned now.
2. Plan said .NET 9; csproj says net10.0. SDK 10.0.400 was already the right
   tool. net10.0 is the recorded truth.
3. dotnet test 80/81: About_documents_are_served asserted the old "The Block"
   name in the served README. Identity assertion, updated to TheYard.
4. Playwright 6/7: smoke.spec.ts expected a dialog heading /The Block/. Same
   class, updated.
5. Dockerfile v1 copied a nonexistent public/ folder. Removed.
6. Dockerfile v1 created a user that already exists: aspnet:10.0 ships a
   built-in non-root "app" user (APP_UID). Using the built-in user IS the best
   practice; creating your own collides.
7. The TargetFramework grep used -m1 under -R, which is per-FILE, so publish
   received six copies of "net10.0". head -n 1 fixed it.
8. About endpoints and the dataset loader resolve files by walking up the tree;
   the publish output does not include them. README.md, docs/, data/ are now
   COPY'd into /app.
9. The header brand in src/App.tsx still read "The Block" after everything else
   was renamed. Caught by eye in the browser, not by any sweep. Now "The Yard";
   the shipped bundle greps The Yard=1, The Block=0.

## Azure answers from Block 0 (Thursday's cost decision needs these)

- Subscription "Azure subscription 1", id df3b718c-6d99-4904-8102-6f865941f640,
  state Enabled, tenant Default Directory (stevenstout11gmail.onmicrosoft.com)
- Account type: FREE TRIAL. quotaId FreeTrial_2014-09-01, spendingLimit On.
  Credit-based: charges cannot run away, and some paid SKUs refuse to deploy
  until an upgrade to pay-as-you-go.
- az group create + delete in centralus both worked, so provisioning is proven.
- Bicep CLI 0.46.1 installed via az bicep install.
- Resource providers are NOT registered yet on this fresh subscription; expect
  one-time "registering namespace" waits on Thursday, that is normal.
- Standing design decision (8/31): Front Door in front of BOTH frontend and
  backend, origin never publicly exposed. Standard tier: lock the origin with
  the X-Azure-FDID header check; Private Link is the Premium-tier version.
  Front Door's health probes kill scale-to-zero; accepted knowingly, cost is
  fine on trial credit, re-decide when the trial ends.

## Explicitly NOT done

- Azure deployment: ACR, compute (Container Apps vs App Service still open),
  Front Door, the infra/ Bicep folder. All Thursday.
- README rewrite beyond the identity scrub and the Docker section.
- Custom domain, deployment pipeline.
- GitHub repo description still reads "Steve Stout Sandbox"; change it to a
  real sentence.
- Playwright printed a psl-5.dll host warning at install; 7/7 pass regardless,
  ignore until it bites.

## Open questions for Thursday morning

- Container Apps or App Service as the Front Door origin? (8/28 note said App
  Service; the Monday plan's Bicep block was written for Container Apps. Pick
  one before writing infra/main.bicep.)
- When the free trial exhausts: upgrade to pay-as-you-go or let it lapse?
- Is the equipment-domain rebrand permanently dead, or a later fork once the
  URL is live?