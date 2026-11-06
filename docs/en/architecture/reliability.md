# Reliability

The goal is that **no recording is ever lost**: if it cannot be transcribed now, its audio is kept so it can be retried later. This page lists every path by which a recording can be left half-done and what happens in each one.

## Unprocessed recordings (`FailedRecordingStore`)

A preserved recording is a folder under `%LocalAppData%\LoopbackTranscriber\failed\`:

The folder name is the recording date plus 8 random characters, for example `20261018_093000_1a2b3c4d`. Inside:

| File | Contents |
|---|---|
| `manifest.json` | `AppName`, `RecordedAt`, `SourceWav`, `MicWav`, `MicStartOffsetTicks`, `Error`, `ErrorKey` and `ErrorArgs`. |
| `meeting.wav` | The main track. |
| `microphone.wav` | The microphone, if there was one. |

- `TryPreserve` creates the folder, writes the manifest and **moves** the WAVs. If it cannot move the microphone, it keeps only the main track and warns. If the main track fails, it undoes what it did and returns `null`, and the originals stay where they were.
- `List` reads the manifests and skips folders without `meeting.wav`. An unreadable manifest raises a notice (`NoticeUnreadableFailedRecording`) and the folder is left untouched.
- The error is stored with its **localization key** (`ErrorKey`, `ErrorArgs`), so the reason is shown in the current language even if it has changed since.
- **Retry** (`RecordingWorkflow.RetryAsync`) processes the recording with the current settings and, on success, deletes the folder. If it fails again, the folder stays as it was. **Discard** deletes it after asking for confirmation.
- `RecordingViewModel` shows, in each application's view, the recordings with that `AppName` and, in every view, those with the default name (`Meeting`).

## Every path, covered

| What happens | Where it is handled | Outcome |
|---|---|---|
| Transcription fails (model, GPU, no text in ≥ 20 s) | `RecordingWorkflow.StopAndProcessAsync` | The audio becomes *unprocessed*, with the reason. |
| No text in a recording of < 20 s | `RecordingProcessor.ProcessAsync` | **The only exception**: not a failure. Nothing is saved and the audio is deleted. |
| The user exits while recording and chooses *No* | `AppExitWorkflow` → `PreserveInterruptedRecordingAsync` (10 s) | *Unprocessed* (`RecordingInterruptedReason`). |
| Windows signs out or shuts down | `MainWindow.OnSessionEnding` → `PreserveInterruptedRecording` (3 s) | *Unprocessed*. |
| Unhandled exception on the UI thread | `CrashHandler` → `PreserveInterruptedRecording` (3 s) | *Unprocessed*. The message confirms it (`CrashBodyPreserved`). |
| Unhandled exception on another thread | `CrashHandler` → rescue on the UI thread (2 s to respond) | *Unprocessed* if there is time. |
| The process dies abruptly (power cut, Task Manager) | `OrphanRecordingRecovery` on the next start | *Unprocessed* (`RecordingRecoveredReason`). |
| It cannot be moved to `failed\` | `CaptureCoordinator.PreserveFailed` | The originals stay in `%TEMP%` and the next start recovers them. |
| The audio driver hangs when stopping | `ProcessLoopbackRecorder.AbandonStuckCapture` | The WAV is closed with what was captured and things carry on. |

The synchronous rescues (`PreserveInterruptedRecording`) are used where `await` is not possible, such as the error handler or `SessionEnding`. They run the work on the thread pool and wait with a time limit. Since they never need the calling thread, they cannot deadlock.

## Orphan recordings

A recording is an **orphan** if its audio is in `%TEMP%\LoopbackTranscriber` but no instance is recording it. `MainViewModel.Start` calls `RecordingViewModel.RecoverOrphanRecordingsAsync`, which runs `OrphanRecordingRecovery.Run`:

1. **Finds** the `recording_*.session.json` files (and `grabacion_*`, the prefix used by older versions) and the WAVs with a recording name that have no `.session.json`.
2. **Skips** those that belong to the current instance (`CaptureCoordinator._ownedBaseNames`).
3. **Defers** those with any WAV in use, checked by opening it with `FileShare.None`. It retries up to 4 times, every 30 s. If they are still in use, they are left for the next start.
4. For each one:
   - Reads the session. Without a `.session.json`, the application is `Meeting` and the date is taken from the file name.
   - Ignores derived WAVs (`.16k.wav`) and those with no data beyond the header (`WavRepair.HasAudioData`).
   - **Repairs the header** of each WAV (`WavRepair.TryRepairHeader`). If the `data` size is 0 or larger than the file, it writes the real size, rounded down to `blockAlign`, and fixes the `RIFF` size.
   - **Picks the track** with the same rule as a normal stop: the process track if its peak is ≥ 0.01; otherwise the output track with the highest peak ≥ 0.01; otherwise the process track even if silent. Only when there is neither a process nor an output track does the microphone become the main track. See [Audio capture](audio-capture.md).
   - Computes the microphone offset from the times saved in the session.
   - Moves the tracks to `failed\` and deletes the rest.
   - If there is no audio, deletes everything silently.
5. Failures are reported with `NoticeOrphanRecoveryFailed`. Their audio stays in `%TEMP%` and they are retried on the next start.

## Settings

`SettingsService` keeps the settings in memory as JSON: each `Snapshot()` is an independent copy, and each `Update(change)` applies the change to a new copy under a lock.

- **Atomic write**: `settings.json.tmp` and then `File.Replace`, or `File.Move` the first time.
- **No pointless writes**: if the JSON has not changed, nothing is written.
- **Retries**: 3 attempts, waiting 100 ms × attempt. If they all fail, the changes stay pending in memory, the user is told once (`NoticeSettingsNotSaved`), and it is retried on the next change and in `App.OnExit` (`FlushPendingWrite`).
- **Damaged file**: renamed to `settings.json.corrupt-<yyyyMMdd-HHmmss>`, defaults are used and the user is told (`NoticeSettingsReset`).

## Errors that mean something to the user

`UserError` creates ordinary exceptions (`InvalidOperationException`, `InvalidDataException`, `IOException`) with their **localization key** and arguments in `Exception.Data`. The `Message` is in English, for the log.

- `UserMessage.From(ex)` recovers the key, and `Localize()` translates it into the current language when displayed.
- An exception without a key is shown as `ErrorUnexpected` with its message.
- Keys travel intact through the worker processes (`ErrorKey` and `ErrorArgs`) and through the unprocessed recordings (`manifest.json`).

## Expected failures and programming errors

`ExpectedFailures` groups the exceptions that count as environment failures:

| Predicate | Exceptions |
|---|---|
| `IsFile` | `IOException`, `UnauthorizedAccessException` |
| `IsAudioFile` | the above, plus `FormatException` and `InvalidDataException` |
| `IsDeviceOrFile` | `COMException`, `InvalidComObjectException`, `IOException`, `UnauthorizedAccessException` |

The code's `catch` blocks filter with these predicates (`catch (Exception ex) when (ExpectedFailures.IsFile(ex))`). A programming error, such as a `NullReferenceException`, is not hidden: it reaches `CrashHandler` and ends up in the log.
