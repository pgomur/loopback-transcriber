# Troubleshooting

## Where to look

| File | Contents |
|---|---|
| `%LocalAppData%\LoopbackTranscriber\logs\app.log` | Log of the application and its worker processes. Each line has the date, level (`INFO`, `WARN`, `ERROR`) and PID. Once it passes 1 MB it is renamed to `app.log.1` and a new one starts. |
| `%LocalAppData%\LoopbackTranscriber\live-metrics.log` | Live translation timings, chunk by chunk, and a `SUMMARY` line at the end of each session. It is emptied once it passes 512 KB. |

When the application closes because of an unexpected error, the message shows the path of the log.

Whisper and the summary model run in **worker processes**: the same `LoopbackTranscriber.exe` started with `--worker <type>`. To tell them apart in Task Manager, open the *Details* tab and add the *Command line* column. The types are `whisper`, `summary`, `whisper-live` and `translate-live`.

## Common problems

### "No text was detected in N s of audio (possible model or GPU failure)"

The recording is 20 seconds or longer and Whisper returned no text. The audio is kept as an **unprocessed recording**.

1. Click **Retry**.
2. If it happens again, update your GPU driver. Whisper uses Vulkan when available.
3. Check in the log which track was used. Look for `falling back to device track` and `ProcessLoopbackRecorder: packets=…, peak=…`. A `peak` of 0 means the application played no audio.

For recordings shorter than 20 seconds, an empty transcript is not treated as an error: nothing is saved and the audio is discarded.

### The application track is silent

Some applications play call audio from a process that is not a child of the main one. In that case the application uses the **audio output** track, which includes any other sound on the computer. To avoid it, add in [Applications](applications.md) the process that shows up under **Detected processes with audio** during the call.

### "The background process ended unexpectedly (code N)"

The Whisper or summary process ended without returning a result. The log includes the last 40 lines of its error output.

- The most common cause is a missing **Microsoft Visual C++ Redistributable (x64)**. Install it from <https://aka.ms/vs/17/release/vc_redist.x64.exe>.
- Code `2` means the process ended because the main application closed.

### "The downloaded model (…) does not match the expected checksum and was discarded"

The download arrived damaged or incomplete. Download it again from **Settings**. If it keeps happening, check whether a proxy or an antivirus is modifying downloads.

### "The model … could not be loaded"

The file exists but cannot be loaded. On the next use its SHA256 is checked again: if it is damaged it is deleted and can be downloaded again.

### "Your microphone is not being recorded"

The default microphone could not be opened. In *Windows Settings → Privacy & security → Microphone*, check that desktop apps have access, and that there is a default microphone.

### "The recovery file could not be created…"

The temporary folder (`%TEMP%\LoopbackTranscriber`) could not be written to. Recording continues, but if the application closes abruptly the recording cannot be recovered. Check free space and permissions.

### "An interrupted recording could not be recovered…"

At startup, audio from an interrupted recording was found but could not be moved to **unprocessed recordings**. The audio is still in `%TEMP%\LoopbackTranscriber` and is retried on every start.

### "The recording could not be started: …"

Capturing the application's audio could not start. The status shows the reason after the colon. Check that the application is still open, select it again and retry. The details are in `app.log`, on the line `RecordingViewModel.StartRecordingAsync: failed to start recording`.

### "The background process could not be started."

The application could not launch a worker process or did not get its answer. Exit the application from the tray and open it again. If it happens again, check `app.log`.

### "The … model could not be checked" or "The installed models could not be checked…"

At startup a model could not be read to check its fingerprint, for example because another program has it open or permissions are missing. The model is not deleted and is checked again before it is used.

### "The summary could not be generated: …" or "The recording could not be transcribed again: …"

The reason appears after the colon and is usually one of the errors on this page, such as a worker process or model failure. "*The audio of this recording was not found.*" means the recording's `.wav` has been deleted or moved.

### "The transcript could not be read…", "The transcript … could not be deleted…" or "The recording could not be deleted…"

The file or folder is open in another program or you do not have permissions on it. Close it and try again. If the search fails for the same reason, the list shows "*The search could not be completed*".

### A transcript does not show up in the history

If a recording folder cannot be read, you are told "*A transcript could not be read and is not shown in the history*", with its path. A folder only appears in the history if it contains a `.md`, `.txt` or `.wav` with the same name as the folder.

## Starting from scratch

1. Exit the application from the tray.
2. Delete `%LocalAppData%\LoopbackTranscriber\settings.json`. To start completely from scratch, delete the whole `%LocalAppData%\LoopbackTranscriber` folder: transcripts and models will be lost.
3. When uninstalling, the uninstaller offers to delete that folder and `%TEMP%\LoopbackTranscriber`.
