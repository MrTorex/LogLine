# LogLine Documentation

**LogLine** is a high-performance, zero-allocation logging framework for Unity 2022.3 LTS and newer.

## Quick Navigation

- **Getting Started & Full Reference:** See [README.md](../README.md) for installation, benchmarks, architecture, API reference, and platform notes.
- **Interactive Sample:** Import **Basic Usage** from **Window → Package Manager → LogLine → Samples** and open `BasicUsage.unity`.
- **Editor Configuration:**
  - Open **Tools → LogLine → Dashboard** for live sink, layout, category, and compile-time stripping controls.
  - Or configure via **Edit → Project Settings → LogLine**.

## Core Concepts at a Glance

1. **Create a cached category logger:**
```csharp
using Log = LogLine.LogLine;
using Logger = LogLine.Core.Logger;

private static readonly Logger LogAI = Log.GetLogger("Combat.AI");
```

2. **Log with zero-boxing generic overloads:**
```csharp
LogAI.Info("Entity #{0} HP={1} Alive={2}", id, hp, isAlive);
```


3. **Strip low-severity logs at compile time:**
Add `LOGLINE_MIN_WARN` (or `LOGLINE_MIN_ERROR`, `LOGLINE_MIN_NONE`) to Scripting Define Symbols via the **Compilation** tab in the LogLine Dashboard.
4. **Route logs per sink:**
Attach `UnityConsoleSink`, `AsyncFileSink` (`PatternLayout` or `JsonLayout`), or your own `LogSinkBase` subclass with `CategoryFilter.Allow(...)` / `CategoryFilter.Deny(...)`.

## Troubleshooting

* **Double-clicking a console log jumps to the wrong line:** Ensure you are using `UnityConsoleSink` (methods are tagged with `[HideInCallstack]`).
* **Logs missing in a release build:** Check `LogLineDefines.CompiledMinimumLevel` and your active `LOGLINE_MIN_*` Scripting Define Symbols for the target platform.
* **File logging on WebGL:** Disable `enableFileLogging` for WebGL builds (WebGL does not support background worker threads or standard local file I/O).
* **Running Package Tests:** Add `"com.mrtorex.logline"` to the `"testables"` array in your project's `Packages/manifest.json` and run EditMode and PlayMode suites in **Window → General → Test Runner**.
