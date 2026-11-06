# Getting started

## Installation

1. Run `LoopbackTranscriberSetup-<version>.exe`. The wizard is available in Spanish, English, Portuguese, German and Russian.
2. If **Microsoft Visual C++ Redistributable (x64)** is missing, the installer downloads and installs it, unless it is already bundled in the installer itself. Whisper and the summary model need it. If it cannot be downloaded, setup continues and tells you where to install it manually.
3. If the Visual C++ installer asks for a restart, the wizard offers it at the end.

The application is installed in `Program Files\LoopbackTranscriber`. Your data is stored in `%LocalAppData%\LoopbackTranscriber`: see [Data on disk](../architecture/data-on-disk.md).

> [!NOTE]
> Only one instance can be open. If you open the application again while it is already running (for example, minimized to the tray), the window of the existing instance is shown.

## First start: downloading the models

On startup, if either of the two models is missing (the first time, for example), the **Before you start** screen is shown:

| Model | Used for | Size |
|---|---|---|
| Whisper large-v3-turbo (GGML, q5_1) | Turning audio into text, and live transcription | ~624 MB |
| Qwen3-4B (GGUF, Q4_K_M) | Summaries and live translation | ~2.5 GB |

1. Click **Download models**. Both models download at the same time. Each download shows its progress and can be **Pause**d, **Resume**d or **Cancel**led. A paused download continues where it left off; **Cancel** deletes what was downloaded.
2. When it finishes, the SHA256 checksum of each file is checked. If it does not match, the file is discarded and you will have to download it again.
3. **Continue** is only enabled once both are ready. Clicking it opens **Settings** so you can check your name and the languages.

The models are stored in `%LocalAppData%\LoopbackTranscriber\models`. You can change that folder in [Settings](settings.md).

> [!TIP]
> On every start, the application checks in the background that the installed models are not damaged. If one is, it deletes it and shows this screen again so it can be downloaded again, unless a recording is in progress or being processed. Details in [Models and integrity](../architecture/models.md).

## Recommended settings before recording

In **Settings**:

- **Your name**: shown in transcripts instead of "You". By default it is the first name of your Windows account.
- **Meeting language**: the language being spoken. If you do not choose one, the application language is used. If your meetings change language, choose **Detect automatically**.
- **Application language**: the language of the interface and of the summaries.

## Consent

Only record and transcribe with the consent of the people taking part. You are responsible for informing them and for complying with the law in your country.
