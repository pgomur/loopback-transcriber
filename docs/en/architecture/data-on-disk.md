# Data on disk

## Locations

| What | Path | Notes |
|---|---|---|
| Settings | `%LocalAppData%\LoopbackTranscriber\settings.json` | Atomic writes. A damaged file is moved aside as `settings.json.corrupt-<date>`. |
| Models | `%LocalAppData%\LoopbackTranscriber\models\` | `whisper\ggml-turbo.bin` and `llm\Qwen3-4B-Q4_K_M.gguf`, each with its `.verified.json`. Configurable. |
| Transcripts | `%LocalAppData%\LoopbackTranscriber\transcripts\` | One folder per application and one subfolder per recording. Configurable. |
| Unprocessed recordings | `%LocalAppData%\LoopbackTranscriber\failed\` | See [Reliability](reliability.md). |
| Log | `%LocalAppData%\LoopbackTranscriber\logs\app.log` | Rotates to `app.log.1` once it passes 1 MB. |
| Live metrics | `%LocalAppData%\LoopbackTranscriber\live-metrics.log` | Emptied once it passes 512 KB. |
| Recording in progress | `%TEMP%\LoopbackTranscriber\recording_<date>*.wav` and `.session.json` | Deleted after processing. Any left behind are recovered at startup. |
| Files for Whisper | `%TEMP%\LoopbackTranscriber\*.16k.wav` | Deleted at the end of each processing run. Any left behind are deleted when their orphan recording is recovered or, if older than 7 days, when the next recording finishes (processed or kept unprocessed). |
| Worker process jobs | `%TEMP%\LoopbackTranscriber\worker\` | `<guid>.job.json`, `<guid>.result.json` and `<guid>.live.json`. Deleted when done. |
| Re-transcriptions | `%TEMP%\LoopbackTranscriber\retranscribe\<guid>\` | Deleted when done. |

## Transcripts folder

Each recording is a folder `transcripts\<App>\<App>_<yyyyMMdd_HHmmss>\`. Inside it, `<base>` is that folder's name:

| File | Contents | When it exists |
|---|---|---|
| `<base>.md` | The transcript, in Markdown. | Always. |
| `<base>.wav` | Copy of the main track. | Always. |
| `<base>_mic.wav` | Copy of the microphone track. | If the microphone was recorded. |
| `<base>.recording.json` | Microphone offset. | If there is a `_mic.wav`. |
| `<base>.summary.json` | The summary. | If one was generated. |

`TranscriptHistoryService.LoadFromDisk` walks the first-level and second-level folders. A folder is a recording if it contains `<folder-name>.md`, `.txt` or `.wav`. The application and the date come from the name (`<App>_<yyyyMMdd>_<HHmmss>`). If the name does not follow that pattern, the whole name is used as the application and the folder's modification date as the date. That way recordings from older versions are read too, whether stored directly in `transcripts\<base>\` or as `.txt`.

`TranscriptsFolderWatcher` watches the folder and its subfolders: files and folders being created, deleted or renamed, not content changes. It reloads the history 500 ms after the last change. If the watcher fails, it is recreated on the next reload.

### `<base>.md`

| Part | Format | Example |
|---|---|---|
| Header | `# <App> - dd/MM/yyyy HH:mm`, on the first line | `# Teams - 18/10/2026 09:30` |
| Paragraphs | `<label>: <text>`, one per turn | `Participantes: Good morning everyone.` · `pgomur (Tú): The integration is done.` |

- The viewer does not show the header as part of the conversation.
- Paragraphs are separated by a blank line.
- The `<speaker>: ` label only appears if the microphone was recorded. The labels are always stored in Spanish. See [Composition](processing.md).
- The viewer turns it into messages with `TranscriptMessageParser`: it drops the header, splits paragraphs on blank lines and recognizes the label with the `SpeakerLabels` pattern. Messages with your label are aligned to the right.

### `<base>.recording.json`

| Key | Type | Meaning |
|---|---|---|
| `MicStartOffsetTicks` | integer | Offset of the microphone relative to the main track, in .NET ticks (100 ns). For example, `1250000` is 0.125 s. |

Only present if there is a `_mic.wav`. Used by re-transcription.

### `<base>.summary.json`

| Key | Type | Meaning |
|---|---|---|
| `Title` | text | Summary title. Replaces the application name in the history. |
| `Summary` | text | The sections, each with its label in the language it was generated in (`Purpose:`, `Key points:`, `Goals:`…) and its items on lines starting with `- `. |

## Temporary files of a recording

| File | Contents |
|---|---|
| `recording_<date>.wav` | Application track (16-bit PCM, 44.1 kHz, stereo). |
| `recording_<date>_console.wav`, `_communications.wav` | Tracks of the default audio outputs (general and communications), in their native format. |
| `recording_<date>_mic.wav` | Microphone, in its native format. |
| `recording_<date>.session.json` | `AppName`, `RecordedAt`, `ProcessStartedUtc`, `DeviceStartedUtc`, `MicStartedUtc`. Makes recovery possible. |

Older versions used the `grabacion_` prefix, which is still recognized.
