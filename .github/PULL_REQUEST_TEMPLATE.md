<!-- Reference the issue this PR resolves, e.g. Closes #12 -->
Closes #

## Description
<!-- Provide a concise summary of the changes made and the motivation behind them. -->

## Type of Change
- [ ] `type: bug` (Fixes an active issue)
- [ ] `type: feature` (Adds new functionality)
- [ ] `type: perf` (CPU/Memory/Zero-alloc optimization)
- [ ] `type: refactor` (Code restructuring without behavior changes)
- [ ] `type: docs` (Documentation, README, or XML comments)
- [ ] `type: chore` (Configuration, package meta, or repository files)

## Affected Area
- [ ] `area: core` (Engine, Buffering, etc.)
- [ ] `area: sinks` (Console, File, etc.)
- [ ] `area: editor` (UI Toolkit Dashboard, Settings, etc.)
- [ ] `area: engine` (Unity compatibility)

## Quality Checklist
- [ ] Code follows project conventions (`.editorconfig`, regions, strict English XML documentation).
- [ ] No regression introduced to existing features.
- [ ] Verified safe behavior with **Enter Play Mode Options** (Domain Reload disabled) (or N/A).
- [ ] Verified **0 GC allocations** on hot logging paths using Profiler / GC assertions (or N/A).
- [ ] Tested in Unity 2022.3 LTS (or specified version if target-specific).
