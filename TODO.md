# CheapShotcutRandomizer - TODO

## Project Focus
MLT/Shotcut project file rendering with melt. AI upscaling/interpolation has been moved to CheapUpscaler.

---

## Cleanup Tasks

### DependencyInstaller Cleanup
- [ ] Remove `InstallVapourSynthAsync()` method
- [ ] Remove `InstallPyTorchAsync()` method
- [ ] Remove `InstallVsMLRTAsync()` method
- [ ] Remove `InstallRifeAsync()` method
- [ ] Remove any AI-related installation strategy logic
- [ ] Update `DependencyType` enum if AI types are defined there

### DependencyChecker Cleanup
- [ ] Remove `DetectPythonAsync()` method (if AI-specific)
- [ ] Remove `DetectVapourSynthAsync()` method
- [ ] Remove `DetectRifeAsync()` method
- [ ] Remove `DetectVsMLRTAsync()` method
- [ ] Remove RIFE variant detection logic
- [ ] Update dependency health calculation to exclude AI dependencies

### FirstRunWizard.razor
- [ ] Remove AI feature introduction/mentions
- [ ] Remove VapourSynth dependency check step
- [ ] Remove Python/PyTorch dependency check step
- [ ] Remove RIFE setup step
- [ ] Update wizard to focus on melt/FFmpeg/Shotcut only
- [ ] Update completion message to mention CheapUpscaler for AI features

### RenderQueueService Cleanup
- [ ] Remove `ExecuteTwoStageRenderAsync()` stubbed method
- [ ] Remove `ExecuteThreeStageRenderAsync()` stubbed method
- [ ] Simplify render type handling to MLT-only
- [ ] Remove intermediate path logic (no longer needed for single-stage)

### General Code Cleanup
- [ ] Remove unused AI-related `using` statements across all files
- [ ] Remove any orphaned AI helper methods
- [ ] Clean up comments referencing removed AI features
- [ ] Verify all AI-related settings in AppSettings are removed or ignored

---

## Completed

- [x] Remove AI UI from Settings.razor (RIFE, Real-ESRGAN, Real-CUGAN sections)
- [x] Remove AI UI from AddRenderJobDialog.razor (checkboxes, settings panels)
- [x] Remove AI variables from AddRenderJobDialog code-behind
- [x] Update validation to MLT-only workflow
- [x] Simplify AddJob to single-stage MLT rendering
- [x] Update BrowseSource to MLT files only
- [x] Update UpdateEstimatedTime for MLT-only
- [x] Update UpdateOutputPath for MLT-only
- [x] Fix _Imports.razor for Shared components
- [x] All 113 tests passing
