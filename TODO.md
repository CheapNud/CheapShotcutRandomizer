<!--
  TODO.md — CheapShotcutRandomizer project work tracker
  Last updated: 2026-08-20

  RULES FOR AI AGENTS:
  - Update the "Last updated" date above whenever you modify this file
  - Items use checkbox format: - [ ] incomplete, - [x] complete
  - Never remove completed items — they serve as history. Move them to "## Done" when a category gets cluttered.
  - Each item gets ONE line. Details go in sub-bullets indented with 2 spaces.
  - Prefix each item with the date it was added: - [ ] (2026-03-17) Description
  - When completing, change to: - [x] (2026-03-17 → 2026-03-18) Description
  - Tag the SOURCE of each item at the end in brackets:
      [code-todo] = from // TODO comment in source code
      [plan] = from a plan document or planning session
      [bug] = from a bug encountered during dev/deploy
      [audit] = from a code audit or review
      [user] = explicitly requested by the user
  - For [code-todo] items, ALWAYS include file:line reference so devs can navigate directly
  - Categories: Blocking, Planned, Future, Done
  - New items go at the TOP of their category
  - Do not create separate TODO_*.md files — everything goes here
  - Keep it terse. If it needs more than 3 sub-bullets, link to a plan document.
  - Do NOT create, rename, or remove categories — the fixed set is: Blocking, Planned, Future, Done
  - When asked for planned work or TODO analysis, ALWAYS include Future items too — list them below Planned and note them as future work
-->

# TODO

## Blocking

_Nothing blocking._

## Planned

_Nothing planned._

## Future

- [ ] (2026-08-09) Fold Core project into main project (or strip to plain Sdk); split not earning its keep [audit]
- [ ] (2026-08-09) Add @key to RenderQueue job list foreach; skip 5s poll when paused and idle [audit]
- [ ] (2026-08-09) BackgroundTaskQueue full+paused hangs Add-job UI call — pass cancellation to WriteAsync [audit]
  - Services/Queue/BackgroundTaskQueue.cs:30
- [ ] (2026-08-09) RenderQueueService minors: progress-throttle races, Task.Run token drops shutdown work, StopAsync delay token, event-subscriber isolation [audit]

## Done

- [x] (2026-08-20 → 2026-08-20) Grid split mode: one compilation carved into duration-balanced consecutive segments across the cells [user]

- [x] (2026-08-17 → 2026-08-17) Grid compilations: generate 2-up or 2x2 split-screen from random tracks via SPR (affine) filters + mix/qtblend transitions, melt-verified [user]
- [x] (2026-08-17 → 2026-08-17) Docs refresh: README/CHANGELOG rewritten for the current app, stale AI-era docs pruned [user]
- [x] (2026-08-09 → 2026-08-17) Prune CI docs bloat (.github/WORKFLOWS*.md + archive, ~2,100 lines about CI) [audit]
- [x] (2026-08-16 → 2026-08-16) Apply global MudBlazor house style: flat theme with hairline borders, two-font typography, PageHeader/ProgressButton components, one-CTA rule, semantic status chips [user]

- [x] (2026-08-09 → 2026-08-13) Temp .mlt files accumulate beside source project on every Add to Queue — add cleanup [audit]
  - Components/Pages/Home.razor:566, Services/ShotcutService.cs:64
- [x] (2026-08-09 → 2026-08-13) Entry.Duration parses In/Out without guard — FormatException on MLT entries missing attributes [audit]
  - CheapShotcutRandomizer.Core/Models/Mlt.cs:80
- [x] (2026-08-12 → 2026-08-12) Post-completion job actions: dropdown in the add-job stepper (do nothing / move output to folder / show in Explorer), move never overwrites, job record follows the file [user]

- [x] (2026-08-10 → 2026-08-10) Show app version at the bottom of the nav drawer (CheapClerk pattern) [user]
- [x] (2026-08-10 → 2026-08-10) Adopt Shotcut's melt invocation shape: consumer as XML element, percent-encoded xml: URL, ?multi:1 when overriding resolution/fps, absolute-path render XML in system temp [audit]
  - Shotcut encodedock.cpp:1366,1622; meltjob.cpp:131-176 — current consumer-args approach works but diverges
- [x] (2026-08-10 → 2026-08-10) Ship MLT's stock export presets (YouTube etc.) — parse key=value files from <melt>/../share/mlt/presets/consumer/avformat [audit]
- [x] (2026-08-10 → 2026-08-10) True pause/resume of running renders via NtSuspendProcess/NtResumeProcess instead of kill+restart [audit]
- [x] (2026-08-10 → 2026-08-10) Retry failed renders once with real_time=-1 (parallel processing is the most common melt failure mode) [audit]
- [x] (2026-08-10 → 2026-08-10) Pre-flight checks at enqueue: missing source files, output-inside-project, self-inclusion, low disk, duplicate output target [audit]
- [x] (2026-08-10 → 2026-08-10) Set autoclose=1 on playlists in render-only temp XML (frees file handles on long playlists) [audit]
- [x] (2026-08-10 → 2026-08-10) Strip shotcut:proxy resources + stale consumer elements from input XML before render (proxy-contamination guard) [audit]
- [x] (2026-08-10 → 2026-08-10) ETA: baseline stopwatch at first progress percent, hide estimate below 2%; add -verbose to melt + per-job log capture; keep-awake (SetThreadExecutionState) during renders [audit]
- [x] (2026-08-10 → 2026-08-10) GOP parity: g=round(fps*5), bf=3, bf=0 for hevc_nvenc/hevc_amf [audit]
- [x] (2026-08-10 → 2026-08-10) UI restructure: queue is the home page, Add Files goes to a full-page stepper, Randomizer and Find Files get their own nav tabs, popup dialog deleted [user]
- [x] (2026-08-10 → 2026-08-10) Respect settings: MaxConcurrentRenders, AutoStartQueue, ShowNotificationsOnComplete now wired; dead DefaultQuality knob removed [user]
- [x] (2026-08-10 → 2026-08-10) Melt parity from Shotcut source research: qscale for QSV, qp_b for AMF, x265-params, audio vbr markers, codec threads, rescale/deinterlacer, real_time cap 4, BelowNormal priority [audit]

- [x] (2026-08-09 → 2026-08-09) Big delete: remove ~4.4k lines of dead code found by audit — see items below [audit]
- [x] (2026-08-09 → 2026-08-09) Delete orphaned AI-installer block from Settings.razor (~1,450 lines, moved to CheapUpscaler) [audit]
- [x] (2026-08-09 → 2026-08-09) Delete dead FFMpegCore stack: FFmpegRenderService, FFmpegInitializationService, VideoValidator, FFmpegErrorHandler + package refs [audit]
- [x] (2026-08-09 → 2026-08-09) Delete FirstRunWizard, DependencyManager UI, DependencyChecker/Installer + Models/Dependency* [audit]
- [x] (2026-08-09 → 2026-08-09) Delete dead Core interfaces, DebugLogger, RenderProgress duplicate, unused Polly pipeline + package [audit]
- [x] (2026-08-09 → 2026-08-09) Untrack stdout.txt/stderr.txt (leaks local paths) and gitignore them [audit]
- [x] (2026-08-09 → 2026-08-09) Fix cancel/pause resurrecting running jobs — retry catch flips killed renders back to Pending [audit]
  - Services/Queue/RenderQueueService.cs:543 generic catch vs MeltRenderService returning false on kill
- [x] (2026-08-09 → 2026-08-09) Fix playlist blank reordering on XML round-trip — `_rawItems` never assigned, entries/blanks re-emitted grouped [audit]
  - CheapShotcutRandomizer.Core/Models/Mlt.cs:94; also breaks FileSearchService timeline math
- [x] (2026-08-09 → 2026-08-09) Stop wiping job DB on transient SQLite errors — narrow the schema-probe catch [audit]
  - Services/DatabaseInitializationService.cs:36; also register DB init before RenderQueueService in Program.cs
- [x] (2026-08-09 → 2026-08-09) Guard async-void timer/event callbacks against component disposal [audit]
  - Components/Pages/RenderQueue.razor:366, Components/Shared/RenderJobCard.razor:376
- [x] (2026-08-09 → 2026-08-09) AddRenderJobDialog: add IMudDialogInstance close path (Cancel/Add inert via DialogService); reset _useCustomRange between opens [audit]
- [x] (2026-08-09 → 2026-08-09) Home.razor generate-random mutates shared project in place — repeat clicks stack playlists; use reloaded copy like queue path [audit]
  - Components/Pages/Home.razor:490 vs :665
- [x] (2026-08-09 → 2026-08-09) Fix pause gate races: pre-dequeue check lets one job slip; Start/Stop/Start leaks a semaphore permit that bypasses Pause [audit]
  - Services/Queue/RenderQueueService.cs:77,409
- [x] (2026-08-09 → 2026-08-09) Replace check-then-set job claim with the transactional ClaimNextJobAsync (kept alive for this) [audit]
  - Services/Queue/RenderQueueService.cs:488, Data/Repositories/RenderJobRepository.cs:45
- [x] (2026-08-09 → 2026-08-09) MeltRenderService: dispose Process, TrySetResult, register cancellation after Start, dispose registration [audit]
  - Services/MeltRenderService.cs:83-144
- [x] (2026-08-09 → 2026-08-09) Move SQLite DB from CWD-relative path to LocalAppData beside settings.json [audit]
  - Program.cs:62
- [x] (2026-08-09 → 2026-08-09) SimulatedAnnealingVideoSelector: empty selection → DivideByZero when first clip exceeds target; use Random.Shared [audit]
  - Services/SimulatedAnnealingVideoSelector.cs:96
- [x] (2026-08-09 → 2026-08-09) Settings dependency grid: typed paths bind to throwaway DTO and are discarded — only browse button persists [audit]
- [x] (2026-08-09 → 2026-08-09) Stranded-Pending on shutdown-during-retry: status written before re-enqueue with cancellable delay between [audit]
  - Services/Queue/RenderQueueService.cs:568-580
