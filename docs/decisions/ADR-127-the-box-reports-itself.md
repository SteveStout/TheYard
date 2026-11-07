# ADR: The box reports itself

Status: accepted, 2026-11-07, at 1.0.4.156. WickedAwsome, the Linux box that runs the job hunt's morning program and the Claude sessions that build this site, gets a card under Cloud infrastructure. Its own agent posts a snapshot once a minute and a live sample every two seconds to the site, behind the operator's key; the site holds them in memory and keeps one minute a snapshot in the plan container.

## Reading list

1. [ADR: One frame for every resource](https://theyard.stevenstout.biz/?doc=adr-one-frame-for-every-resource): the frame the card is drawn in, its dials, its rows and its readout.
2. [ADR: The Nightly Function on the Admin tab](https://theyard.stevenstout.biz/?doc=adr-the-nightly-function-on-the-admin-tab): the first card fed by a push with the operator's key, whose route this one copies.
3. [ADR: Every Claude lane reports its usage](https://theyard.stevenstout.biz/?doc=adr-every-claude-lane-reports-its-usage): a document held in this process and kept in the plan container under kinds of its own.
4. [ADR: Site activity, and the line an address does not cross](https://theyard.stevenstout.biz/?doc=adr-activity): why the box's posts stay out of the activity card, and why no address reaches the wire.

## In plain words

The job hunt does not only run in Azure. Every morning at six a program on a Linux box at home drafts and files, and the sessions that write this site run there too. Until now the Admin tab watched every Azure resource and said nothing about the machine the work actually runs on. Now the box sends the site what Linux's own System Monitor shows (each core, the memory and swap, the network, every disk and the array under them, both GPUs, every temperature and the services the job hunt needs) and the card draws it in the same frame as every other resource. If the box goes quiet for three minutes, its row turns red.

What that is worth: the one machine the job hunt cannot run without is watched the way the cloud is, and a failed disk in its array, a stopped service or a box that stopped reporting shows on the same tab, in the same colour, as a throttled database.

## Context

On 4 November Steve asked for "a module similar to this for wicked awsomse for our server because it dose support the yard's job hunt", then "also I want all information on the resources" and "any way we can make the monitors live like on linux resources", with a picture of System Monitor's Resources tab. The box is an i3-8350K with four cores, 32 GB, two RTX 2080s and a RAID0 of six SSDs with no redundancy (J:\server\DONE-2026-10-26.md): any one failing loses the system.

## Decision

1. **Push, not pull.** The box posts to the site; the site never reaches into a home network. One user unit, `theyard-box-agent.service`, runs a Python script with the standard library only, reading /proc, /sys, `df`, `nvidia-smi` and `systemctl`, with no package installed and no sudo.
2. **Two cadences, one connection.** Every two seconds a small live sample (each core, memory, swap, each interface's rates, each GPU's load) goes to `POST /api/admin/cloud/box/live` and lands in a ring of sixty in this process's memory; every thirtieth tick the full snapshot goes to `POST /api/admin/cloud/box/snapshot`. Both are behind the operator's key in `X-Admin-Key`, read from a file of mode 600 in the box owner's home, never in the repository, never logged. The agent posts to the web app's own name, not through the edge, so the stream costs no edge credits.
3. **Held in memory, kept a minute at a time.** The newest snapshot and the ring live in this process, which serves both names. The recorder keeps one minute per snapshot as `box-minute` and the snapshot's slow facts as `box-hour` in the plan container, which expires every document after thirty-one days, so there is no new container and no new kind of expiry.
4. **The red rule, with its guard.** No snapshot for three minutes turns the row "Is the box reporting?" red, which turns the card and its rail dot red. The silence is measured from the later of the newest snapshot (held or kept) and this process's start, so a roll, two to five minutes with nothing listening, never reads as the box being down. A live sample older than ten seconds reads blue on the live panels only.
5. **The thresholds are the box's own.** All cores amber at 70 % and red at 90 %, held fifteen minutes (the local gate takes the i3 to 100 % for about eight); memory 85 and 95; any file system 80 and 90; each GPU's memory 85 and 95; every temperature amber ten degrees under its sensor's own high mark and red at it, the marks read from the sensors and `nvidia-smi`, never typed; the array red at once when a member is missing or failed; a service that must run (the remote-control session, the J: mount, Tailscale, ssh and the 6 AM timer) red at once when it is not running, and amber when the morning run's last result failed. Sunshine is known to be off and reads as information.
6. **The box's posts are not visitors.** An accepted post under `/api/admin/cloud/box/` stays out of the request ring, the activity card and the kept log; a refused one stays in, because the operator's key was tried. Every request under that prefix is left out of Application Insights' request telemetry, so forty-three thousand posts a day spend nothing of its 0.1 GB daily cap.
7. **No address and no port on the wire.** The snapshot may carry where the box sits on a network; the card names interfaces by name only, and every word from the box passes one scrub (`BoxWords.Scrub`) that replaces any address and any port before it reaches a public page.

What was considered: Azure Arc (a sign-in on the box, more on the box, and Log Analytics ingestion billed), streaming the samples (the edge buffers a streamed answer, the option ADR-100 rejected) and a Cosmos DB kind of its own (the plan container already expires at thirty-one days).

## What holds it

`BoxSnapshotRulesTests` holds every rule's sentence, the list limits and the three handlers over the in-memory ports; `BoxCardTests` the eight rows waiting before a snapshot, the three minutes and the guard, each red case and the scrub; `BoxRoutesTests` the two posts' 401, 400 and 200, the anonymous live read, and both exclusions; `cloud.spec.ts` the card's eight rows in the one frame.

## Where it sits

The records in `TheYard.Data`, the rules in the Domain's Admin feature, the ports and handlers in the Application's Cloud feature, and the source, the card, its rows, its words and its routes in the host's Cloud slice beside the network log's. The agent and its unit are in `scripts/box-agent/`.

```live path=api/TheYard.Api/Features/Admin/Cloud/BoxRows.cs region=box-rows
```

## Files

- [`api/TheYard.Data/BoxSnapshot.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Data/BoxSnapshot.cs) - what the box posts
- [`api/TheYard.Domain/Features/Admin/BoxSnapshotRules.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Domain/Features/Admin/BoxSnapshotRules.cs) - what it may hold
- [`api/TheYard.Application/Features/Admin/Cloud/BoxPorts.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Admin/Cloud/BoxPorts.cs) and [`BoxHandlers.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Application/Features/Admin/Cloud/BoxHandlers.cs) - the ring, the snapshots and the three handlers
- [`api/TheYard.Api/Features/Admin/Cloud/BoxCard.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/BoxCard.cs), [`BoxRows.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/BoxRows.cs), [`BoxSource.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/BoxSource.cs) and [`BoxRoutes.cs`](https://github.com/SteveStout/TheYard/blob/main/api/TheYard.Api/Features/Admin/Cloud/BoxRoutes.cs) - the card and its routes
- [`scripts/box-agent/`](https://github.com/SteveStout/TheYard/tree/main/scripts/box-agent) - the agent, its unit and its installer
