# Changelog

All notable changes to **LogLine** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-11-25

### Added
- **Core Logging Pipeline:** Thread-safe `Logger` (`Trace`, `Debug`, `Info`, `Warn`, `Error`, `Fatal`), `LogLine` static facade, and `LogLineCore` copy-on-write sink dispatcher with fault isolation.
- **Zero-Allocation Hot Paths:** Stack-allocated `Span<char>` `FastFormatter` (generic overloads for up to 3 arguments with zero boxing via `UnsafeUtility.As`), zero-alloc disabled path checks, and zero-alloc layout formatting under Mono and IL2CPP.
- **Compile-Time Stripping:** `[Conditional]` attributes backed by `LOGLINE_MIN_*` scripting define symbols (`LOGLINE_MIN_TRACE` through `LOGLINE_MIN_NONE`) with `CompilationDefineUtility` and build-time validation.
- **Built-In Sinks & Routing:**
  - `LogSinkBase` base class with thread-safe `IsEnabled`, `MinimumLevel`, immutable `CategoryFilter` (`AllowList` / `DenyList`), and single-execution disposal.
  - `UnityConsoleSink` with `[HideInCallstack]` and Editor Console double-click navigation (`ConsoleDoubleClickHandler`).
  - `AsyncFileSink` backed by a lock-free MPSC `LogRingBuffer`, 64 KB `StreamWriter` buffer, immediate `Error`/`Fatal` flush, periodic flush, overflow accounting, size-based rotation with archive retention, and `FilePathResolver` macro expansion.
- **Layouts:** Compiled `PatternLayout` (rich-text console and plain file patterns, custom zero-alloc date formatter) and flat RFC 8259 `JsonLayout` (NDJSON) with configurable field toggles and bounded `InnerException` serialization.
- **Unity Integration & Editor Tools:**
  - `LogLineSettings` ScriptableObject with longest-prefix `CategoryRule` resolution and live `ApplySettings` hot-reconfiguration.
  - `UnityLogInterceptor` for capturing native `UnityEngine.Debug.Log*` calls under the `"Unity"` category.
  - Full support for **Enter Play Mode Options** (Domain Reload disabled).
  - UI Toolkit Editor Dashboard (`Tools -> LogLine -> Dashboard`), `Project Settings -> LogLine` provider, and custom Inspector.
- **Tests, Samples & Documentation:**
  - EditMode and PlayMode test suites (`LogLine.Editor.Tests`, `LogLine.Runtime.Tests`) including 10×10k multithreaded stress tests, strict `GC.Alloc` Profiler verification, and `Debug.Log` comparative benchmarks.
  - Importable UPM `Samples~/BasicUsage` interactive scene with custom `InMemoryOverlaySink`.
  - Complete `README.md`, `Documentation~/index.md`, and package branding.

[Unreleased]: https://github.com/MrTorex/LogLine/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/MrTorex/LogLine/releases/tag/v1.0.0
