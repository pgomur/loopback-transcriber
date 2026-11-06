# Recording a meeting

## Choosing the application

The sidebar shows the applications being watched. **Teams** is included by default. You can add any other application that plays audio (Zoom, the browser for Meet, Discord…) from [Applications](applications.md).

The list is refreshed every 1.5 seconds. An application can only be recorded while it is open. If it is, the status says "*X detected. Press "Start recording" to start.*". If not, "*X doesn't seem to be open yet. Open it and select it again.*".

## Starting and stopping

1. Select the application and click **Start recording**. The status changes to "*Listening to X...*" and the timer starts.
2. Click **Stop recording** when you are done. The recording is processed straight away:
   - "*Processing audio...*"
   - "*Checking the transcription model...*"
   - "*Transcribing... N%*"
   - "*Transcript ready and saved*"
3. The transcript appears in the viewer and is saved to the history.

You can also start and stop from the **tray icon**: right-click → *Start recording X* / *Stop recording X*. The icon only appears while the window is minimized or closed with the **X**. Stopping from the tray shows the window, so you can follow the processing. While recording, hovering over the icon shows "*Loopback Transcriber - Recording X*".

> [!NOTE]
> If you close the recorded application while recording, the recording stops and is processed as if you had clicked **Stop recording**.

## What is recorded

Three sources are recorded at the same time:

| Track | What it contains | What it is for |
|---|---|---|
| Application | Only the audio played by the chosen process and its child processes | The voices of the other participants. It is the main source. |
| Audio output | Everything played through the Windows default outputs: the general one and, if it is a different one, the communications one. Each on its own track. | Fallback if the application track turns out silent. |
| Microphone | The default microphone | Your voice, on a separate track. |

When recording stops, if the application track has no signal, the loudest output track is used instead. The microphone is transcribed separately and aligned in time with the other track. That is how the transcript tells **Participants** apart from **You**, or your name followed by "(You)".

If the microphone cannot be opened, recording continues without it and you are warned: "*Your microphone is not being recorded: the transcript will only include the participants.*". If it opens but only captures silence, its track is dropped when recording stops and the transcript has no speaker labels.

## While a summary is being generated

You cannot start recording while an AI summary is being generated, because both compete for the GPU and memory. The button says so: "*You can't record while an AI summary is being generated.*"

## If no text is detected

If Whisper returns no text:

- **Recording shorter than 20 seconds**: it is assumed nothing was said. The viewer shows "*(No text was detected in the audio)*", the status changes to "*Transcript ready.*" and **nothing is saved**: neither text nor audio.
- **Recording of 20 seconds or more**: it is treated as a failure (see below) and the audio is kept.

## If something goes wrong

Except in the case above, the application **never deletes the audio** of a recording that could not be transcribed. If processing fails, the status shows the error and the folder where the audio was saved, and the application's view shows the **Unprocessed recording** banner with two icon buttons, which show their name on hover:

- **Retry**: processes it again with the current settings. Not available while recording, while another recording is being processed or while a summary is being generated.
- **Discard**: deletes the audio for good, after asking for confirmation.

Each unprocessed recording shows up in its application's view. Those recovered without knowing which application they belonged to show up as "Meeting" in every application's view.

The same happens if the application closes during a recording, the computer shuts down or an unexpected error occurs. When you open it again, the recording shows up as *unprocessed*. The specific cases are in [Reliability](../architecture/reliability.md).

## Closing the application

- The window's **X** does not close the application: it **hides it to the tray**, so you can keep recording. The first time, a notification explains this. Clicking the icon shows the window again.
- To really exit: right-click the tray icon → **Exit**.
- If you exit while recording, you are asked what to do:
  - **Yes**: stop the recording, process it with the window visible, and exit when done.
  - **No**: exit now and keep the audio unprocessed for next time.
  - **Cancel**: keep recording.
- If a recording is being processed, you are asked whether to wait for it to finish and then exit. If you say no, the application stays open.
