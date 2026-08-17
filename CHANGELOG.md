# Changelog

All notable changes to Cheap Shotcut Randomizer are documented here, grouped per minor release.

## [2.8.x] - 2026-08

### Added
- House UI style: flat theme with hairline borders, surface-contrast light/dark palettes, Bahnschrift/Segoe UI typography, page headers with kicker/title/actions, progress buttons on all async actions, semantic status colours
- Searchable export-preset picker with recommended presets pinned on top
- Hardware encoders listed above CPU encoders in the encoder selection
- App version shown at the bottom of the navigation drawer
- Release retention: the release workflow keeps only the newest 3 releases (tags always remain)

### Changed
- Hardware capabilities panel moved from the Render Queue to Settings

## [2.7.x] - 2026-08

### Added
- Post-completion job actions: move output to a folder or reveal in Explorer
- Added-on timestamp on render queue job cards

### Changed
- Generated shuffle/random projects moved to the app temp folder and are deleted once their job finishes (with a startup sweep for crash leftovers)
- Melt invocation adopts Shotcut's exact shape: consumer embedded as an XML element, percent-encoded producer URL, multi consumer for resolution/frame-rate overrides, absolute-path render XML
- Forge workflows moved from `.gitea/` to `.forgejo/`

### Fixed
- `Entry.Duration` no longer throws on MLT entries missing in/out attributes

## [2.5.0 - 2.6.0] - 2026-08

### Added
- True pause/resume: pausing a render suspends the melt process in place instead of killing it
- Stock MLT/Shotcut export presets offered in the render stepper
- Pre-flight checks on the review step: missing media files, duplicate output targets, low disk space
- Failed renders retry once in single-threaded safe mode
- Keep-awake during renders; honest ETA (baselined at first progress, hidden below 2%)
- Proxy-resource guard and playlist autoclose on render XML

### Changed
- Encoder quality flags verified against the Shotcut source: `qscale` for QSV, `qp_i/qp_p/qp_b` for AMF, `x265-params` for x265, audio `vbr` markers, codec `threads`, Shotcut-parity GOP/B-frames, `real_time` capped at 4, melt runs at below-normal priority

## [2.4.0] - 2026-08

### Changed
- Queue-first UI: the render queue is the home page; adding a job is a full-page stepper (Source → Video → Audio → Tracks & Range → Review); the popup dialog was removed; Randomizer and Find Files became their own pages
- Settings that were silently ignored now take effect: max concurrent renders, auto-start queue, completion notifications; the dead quality dropdown was removed

## [2.2.x - 2.3.0] - 2026-08

### Added
- Video encoder selection: x264/x265 plus detected NVENC/QSV/AMF hardware encoders
- Resolution, aspect ratio and frame rate overrides; audio codec, bitrate and quality-based VBR
- Added-date display on queue jobs

## [2.1.0] - 2026-08

### Removed
- ~5,000 lines of dead code left from the CheapUpscaler split (orphaned AI installer, unused FFmpeg render stack, duplicate dependency UIs) and the SharpCompress/FFMpegCore/Polly package references

### Fixed
- Cancelled/paused jobs no longer resurrect through the retry path
- Playlist blanks keep their timeline position across XML round-trips
- Transient SQLite errors no longer wipe the render queue database
- Queue pause races, atomic job claiming, component disposal guards, and a dozen smaller audit findings

## [2.0.0] - 2026-08

### Changed
- Upgraded to .NET 11 and current package versions; migrated to slnx; SharpCompress 0.50 API migration
- Repository moved to forge-first hosting with an automated release pipeline
