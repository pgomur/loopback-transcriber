# Transcripts and summaries

## Where they are

- **Transcripts** (sidebar): every saved transcript, from any application.
- **History**, in each application's view: only that application's transcripts.

Each item shows the title (the summary's title if there is one, otherwise the application name), the date and audio duration (`dd/MM HH:mm · m:ss`, or `h:mm:ss` from one hour on) and a 100-character excerpt. The excerpt comes from the summary if it exists and otherwise from the beginning of the transcript. If there is neither, it shows "*(Empty transcript)*". The list refreshes on its own when the transcripts folder changes, including when you copy or delete files by hand.

**Open folder** opens the transcripts folder in Explorer.

> [!NOTE]
> An application's **History** shows the recordings whose folder carries exactly its name. If the application name contains characters that are not allowed in a file name (`\ / : * ? " < > |`), its recordings are saved with those characters replaced by `_` and only show up in **Transcripts**.

## Searching

Both lists have a **Search transcripts and summaries** box. It searches the title, the application name, the date (`dd/MM/yyyy HH:mm` and `yyyy-MM-dd`), the summary and the full text of the transcript.

- Case- and accent-insensitive: `resume` finds "Résumé".
- Several words: **all** of them must appear, anywhere.
- In quotes, an exact phrase: `"next week"`.
- The search starts 300 ms after you stop typing. If nothing matches, the list says "*No results*".

## Filtering

The **Filter** button opens a panel to narrow the list:

- **From** / **To**: dates included.
- **Minimum duration (minutes)**: leaves out shorter recordings. Recordings without saved audio are left out too while this filter is set.

Filters apply immediately. **Apply** closes the panel and **Clear filters** removes them all. While a filter is active, the **Filter** button is highlighted. Each list (**Transcripts** and each application's **History**) has its own filters and its own search.

## The viewer

When you open a transcript, the viewer has two tabs:

- **Transcript**: the text as a conversation. Each paragraph shows who is speaking (**Participants**, **You** or *your name (You)*). As in a chat, your turns are on the right and the participants' on the left.
- **AI summary**: the summary, if there is one.

**Clear viewer** closes it. If the recording only has audio, the viewer says so instead of showing text.

### Generating a summary

In the **AI summary** tab, click **Summarize**. Progress is shown as a percentage. The summary is written in the **application language**, even if the meeting was in another one, and contains:

- **Title**: eight words at most.
- **Purpose**: what the conversation is about and why it took place.
- **Key points**: between 5 and 12, with concrete details.
- **Topics**: only if several subjects were discussed, or one in a lot of detail.
- **Goals**: what someone wants to achieve or commits to doing.
- **Next steps**: meetings, dates and follow-ups that were agreed.

Sections with no content are left out. Once generated, the summary's title replaces the application name in the list. **Regenerate** replaces the summary with a new one.

Section titles are always shown in the current application language, each with its icon. The content stays in the language it was generated in.

> [!NOTE]
> Summaries cannot be generated while recording or while a recording is being processed. If you close the application while one is being generated, you are asked to confirm, because that summary would be lost.

If the summary model is missing, the viewer says "*The summary model is missing. Download it in Settings to generate summaries.*". A recording that only has audio cannot be summarized.

AI summaries can contain errors: review them before using them.

### Transcribing again

**Transcribe again** processes the saved audio again with the current settings (language, your name) and replaces the text. It asks for confirmation first. Use it, for example, after changing the meeting language.

- It is only in the viewer of each application's **History**, not in **Transcripts**.
- It is only available if the recording has its audio (`.wav`), and not while recording or while another recording is being processed.
- The summary is **not** changed. You can regenerate it afterwards.
- The file's header line is kept.

## Deleting

Tick the boxes of the transcripts you want to delete and click **Delete**. You are asked to confirm. The whole folder of each recording is deleted: text, audio and summary. If it was the last one for that application, the application folder is deleted as well. This cannot be undone.

A transcript cannot be deleted while its summary is being generated.

## Files of each recording

Each saved recording is a folder inside the transcripts folder:

```
transcripts\
  Teams\
    Teams_20261018_093000\
      Teams_20261018_093000.md               text (Markdown)
      Teams_20261018_093000.wav              meeting audio
      Teams_20261018_093000_mic.wav          your microphone, if it was recorded
      Teams_20261018_093000.recording.json   microphone offset
      Teams_20261018_093000.summary.json     summary, if one was generated
```

The format of each file is described in [Data on disk](../architecture/data-on-disk.md).
