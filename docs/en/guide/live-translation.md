# Live translation

Live translation opens a **floating window** that shows, while recording, what the participants are saying and its translation. It helps you follow a meeting in a language you do not master.

## Opening it

- From the application view, with the **Live translation** button. It is only enabled while the application is open. If the floating window is already open for that application, the same button closes it.
- From the tray icon (with the main window minimized or hidden): right-click → **Live translation for X**. While the window is open, the same entry becomes **Close live translation for X**.

Only one floating window can be open at a time: opening it for another application closes the previous one. It appears against the right edge of the screen, vertically centered.

## Using it

1. In **Translate to:**, choose the target language: Spanish, English, Portuguese, German or Russian. The choice is remembered.
2. Click **Start recording and translating**. A normal recording of that application starts and the window begins showing text within a few seconds.
3. Click **Stop recording** to stop. The recording is processed like any other, the window shows the progress, and the full transcript is saved to the history.

If the recording of that application is started some other way (the main window or the tray) while the floating window is open, live translation starts anyway.

While someone is speaking, a **draft** appears and keeps being corrected. When there is a pause, the sentence is fixed as a final line and translated. Long sentences are split over several lines.

The status line shows "*Recording · 00:00*" and, while the models load, "*Loading the speech model…*" and "*loading the translator…*". When not recording, it shows "*Idle*".

### Window controls

| Control | What it does |
|---|---|
| Opacity | Makes the window more or less transparent, between 30% and 100%. Remembered. |
| Always on top | Keeps the window above all others. On when the window opens. |
| Open Loopback Transcriber | Shows the main window. |
| Close | Closes the floating window. Any recording in progress keeps going. |

### Technical data

When **Show technical data** is on, a strip at the bottom shows:

- **Signal**: the level of the application audio, or "*no audio from the app*".
- **Last chunk** (or **draft**): seconds of audio, seconds of processing, and the delay since that speech ended.
- **Translation**: translation time and delay.

## Settings

In the sidebar, **Live translation** opens the floating window settings, with a preview:

| Setting | Default |
|---|---|
| Translate to | The last one chosen; the first time, the application language |
| Show the original text below the translation | Yes |
| Text size | 19 (from 14 to 32) |
| Window opacity | 100% (from 30% to 100%) |
| Show technical data (signal and timings) | Yes |

Changes apply immediately to the floating window, if it is open.

## Limitations

- **Only the application audio is transcribed live**, not your microphone. Your voice does appear in the final transcript.
- Translation uses the same model as summaries (Qwen3-4B). If it is not downloaded, the window only shows the transcription and says "*no translation: the summary model is missing*".
- If the speech model is missing, the window says "*The speech model is missing. Download it in Settings.*".
- If the computer cannot keep up, the oldest chunks are dropped first so the delay does not build up. A sentence may be missing from the window, but never from the final transcript, which is made afterwards from the full audio.

How it works internally is described in [Architecture: live translation](../architecture/live-translation.md).
