# Verification

Verified on 2026-11-07, Windows 11 build 26200 x64; SDK 10.0.302; included runtime 10.0.10.

## Engine checks

45 executable assertions cover actual PNG/JPEG encode/decode; proportional sizing and no enlargement; safe suffixes; input/destination/count limits; existing and duplicate names; original byte hashes; PNG alpha; JPEG white flattening; a concurrent destination collision; temporary-file removal; cancellation before current/later output and after a completed output; all eight EXIF orientations by upright dimensions and top-left pixels; stripped orientation metadata; corrupt input; APNG rejection; the 40-million-pixel limit; the 80-MiB limit; and a source deleted after planning.

Run with `dotnet run --project tests/EngineTests.csproj -c Release`. These are deterministic regression assertions, not exhaustive codec, performance or color-accuracy certification.

## Packaged artifact

The compiled single-file EXE and four original samples were copied to an isolated OS-temporary folder without source, development dependencies or neighboring build outputs. Its built-in diagnostic mode completed 23 integration assertions, including real JPEG and PNG batches, byte-preserved originals, output dimensions, repeat-run numbered copies, invalid settings, corrupt input with queue preservation, source-folder protection, inactive JPEG settings, dispatcher cancellation, terminal row states, no remaining temporary files, removed-destination failure and recovery, per-image processing failure with continued progress, and clearing old result statuses when replanning.

Ten actual WPF client-area renders cover empty, planned, completed, invalid settings, rejected input, protected folder, minimum window, cancelled, recovered and failed-item states. Rendered layouts were inspected and styling/capture defects corrected. Layout sizes include the default 1260 × 900 outer window and minimum 1020 × 740 outer window; the settings pane scrolls at the minimum height. The screenshot in this documentation is generated from the real rendered window, not an HTML mockup.

The application was executed on Windows; these are in-process integration tests of its real window and worker. Actual file pickers, drag-and-drop input delivery, keyboard/mouse events, Explorer launch and signed/unsigned first-run Windows prompts were not automated. No claims of those checks, screen-reader support, other platforms, machines without a preinstalled .NET SDK, or full accessibility conformance are made. The EXE is configured self-contained and was executed from its isolated folder; the host also has .NET installed.

There is no throughput benchmark or maximum-batch memory benchmark. Thumbnail cache is capped per image; large decoded images and a full queue can still consume substantial memory. Physical disk exhaustion, power interruption and network filesystem semantics remain unverified.
