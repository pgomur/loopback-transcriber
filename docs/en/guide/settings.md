# Settings

## The settings screen

### General

| Setting | What it does | Default |
|---|---|---|
| **Your name** | Labels what you say as "*Name (You)*" instead of "*You*". Up to 20 characters. Saved 0.7 s after you stop typing. If you leave it empty, "*You*" is used. | The first name of your Windows account or, if it has none, your user name with a capital initial. |
| **Application language** | Language of the interface and of the summaries: Spanish, English, Portuguese, German or Russian. Applies immediately. | The Windows language if it is one of those five, otherwise English. |
| **Meeting language** | The language being spoken, for your microphone and the participants: **Detect automatically** or one of the five languages. | Follows the application language until you choose another one. |
| **Translucent interface** | Applies the Windows 11 Mica effect to the window background. Not applied on the first-run screen. | Off |

> [!TIP]
> Setting a fixed meeting language gives better results than automatic detection if you always speak the same language. Use **Detect automatically** if it changes from one meeting to the next. How detection works is explained in [Processing a recording](../architecture/processing.md#language).

### AI models

Two sections, **Transcription** and **AI summary**, show each model, its size on disk or that it is missing, and a button to download it: **Download transcription model** and **Download summary model**.

- If a model is already in the folder, its integrity is checked instead of downloading it again.
- The status refreshes on its own when files appear in or disappear from the models folder.
- Downloads cannot be paused here. If one is interrupted, for example by closing the application, the next download continues where it left off.

### Folders and saving

| Folder | Default |
|---|---|
| Models folder | `%LocalAppData%\LoopbackTranscriber\models` |
| Transcripts folder | `%LocalAppData%\LoopbackTranscriber\transcripts` |

**Open** shows it in Explorer and **Change** picks another one.

> [!IMPORTANT]
> Changing a folder **does not move** its contents. If you change the models folder, move the files yourself or download them again. If you change the transcripts folder, the history will only show what is in the new one. On uninstall, folders you changed are not deleted.

### About

A link to the author's GitHub profile, the legal notice (consent to record, local processing, and that summaries can contain errors) and **Third-party software licenses**, which opens `THIRD-PARTY-NOTICES.txt`.

## The settings file

All settings are stored in `%LocalAppData%\LoopbackTranscriber\settings.json`. The application writes it by itself and you do not need to edit it. It is described here for troubleshooting, and because three advanced settings only exist in this file.

| Key | Type | Meaning |
|---|---|---|
| `ModelSize` | string | Whisper model. Defaults to `turbo`, the only one with a pinned SHA256 checksum. Any other value (`tiny`, `base`, `small`, `medium`, `large`) only works if `ggml-<value>.bin` is already in `models\whisper`, and it is **not verified**. If it is not there, an error is raised. The first run and the startup verification always use `turbo`, and any change saved from **Settings** resets this value to `turbo`. |
| `ModelsFolder` | string | Models folder. |
| `TranscriptsFolder` | string | Transcripts folder. |
| `MeetingLanguage` | string | `""` follows the application language, `auto` detects the language, and `es`, `en`, `pt`, `de` or `ru` fix one. |
| `UiLanguage` | string | `es`, `en`, `pt`, `de` or `ru`. |
| `TranslucentUi` | boolean | Translucent interface. |
| `UserName` | string | Your name in transcripts. In the labels, colons are replaced with spaces. |
| `UserNameInitialized` | boolean | The Windows name has already been suggested. It is not suggested again even if you clear the name. |
| `TrayHintShown` | boolean | The "still running in the tray" notification has already been shown. |
| `FirstRunSetupCompleted` | boolean | The initial model download has been completed. |
| `CustomApps` | list | Added applications: `Id` (`custom:<lowercase process>`), `DisplayName` and `ProcessName`. |
| `LiveTranslationLanguage` | string | Target language of live translation. |
| `LiveWindowOpacity` | number | Opacity of the floating window, from 0.3 to 1. |
| `LiveShowOriginal` | boolean | Show the original below the translation. |
| `LiveShowMetrics` | boolean | Show the technical data strip. |
| `LiveTextSize` | number | Text size of the floating window, from 14 to 32. |
| `LiveAudioContext` | integer | **Advanced.** Whisper audio context size for live transcription. `0` uses the model's own. Smaller values reduce latency at the cost of accuracy. |
| `LiveFlashAttention` | boolean | **Advanced.** Turns on *flash attention* for live Whisper. If the model does not load with it, it is retried without it. |

If you edit the file by hand, do it with the application closed. The application only reads the file at startup and, on every settings change, rewrites all of it from what it holds in memory, so your changes would be lost.

### What happens if the file is damaged

- If `settings.json` is not valid JSON, it is moved aside as `settings.json.corrupt-<date>`, the defaults are used and you are told: "*The settings could not be read and have been reset to their defaults.*"
- If it cannot be written (full disk, no permissions, antivirus), it is attempted three times. The changes are kept in memory, retried on the next change and on exit, and you are told once.

Writing is atomic: a `settings.json.tmp` is written and then replaces the original. A power cut never leaves a half-written file.
