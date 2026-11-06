# Models and integrity

## The two models

| | Transcription | Summary and translation |
|---|---|---|
| Model | Whisper large-v3-turbo, GGML q5_1 | Qwen3-4B, GGUF Q4_K_M |
| Source | `huggingface.co/sandrohanea/whisper.net`, `q5_1` folder | `huggingface.co/Qwen/Qwen3-4B-GGUF` |
| Pinned revision | `79dea24237e62d8a6e20a978ba415446d19712b3` | `bc640142c66e1fdd12af0bd68f40445458f3869b` |
| SHA256 | `05265418cae607e28c62db4095612e2c1967885d87cbda5a104834414b563ac8` | `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5` |
| Pinned in | `WhisperTranscriptionService.ModelRevision` and `PinnedSha256` | `SummarizationService.ModelRevision` and `ModelSha256` |
| Local file | `<ModelsFolder>\whisper\ggml-turbo.bin` | `<ModelsFolder>\llm\Qwen3-4B-Q4_K_M.gguf` |

The URLs point to a **specific commit** (`…/resolve/<revision>/…`), not to `main`. If the source repository is updated, the application keeps downloading exactly the same file.

## Download

`EnsureModelDownloadedAsync` works the same way in both services:

1. If the file already exists and passes verification (see below), it is done.
2. Downloads to `<destination>.download` in 1 MB blocks. If a `.download` from an earlier attempt already exists, it **resumes** with a `Range` header; if the server does not answer 206, it starts from scratch. The `.download` is only kept when the download is cancelled (which is what *Pause* does) or the application closes halfway. A network or disk error deletes it.
3. Reports progress at most every 200 ms.
4. If the bytes received do not match `Content-Length`, throws `ErrorDownloadIncomplete`.
5. Computes the **SHA256** of the `.download`. If it does not match, deletes it and throws `ErrorModelHashMismatch`.
6. Moves the `.download` to the destination and writes the verification marker.

For Whisper, if the download fails it is retried once before throwing `ErrorModelDownloadFailed`. A model **without a pinned hash** is never downloaded: `ErrorModelNotPinned` is thrown.

## Verifying without recomputing the hash every time

Computing the SHA256 of 2.5 GB takes a while. `ModelIntegrity` stores a `<model>.verified.json` marker next to each model:

| Field | What it stores |
|---|---|
| `Sha256` | The hash the file was checked against. |
| `Length` | The file size in bytes when it was checked. |
| `LastWriteTimeUtcTicks` | The file's modification date (UTC, in ticks) when it was checked. |

`EnsureVerifiedAsync(path, expectedHash)`:

1. If the marker matches the expected hash and the file's current size and modification time, the model **is accepted without reading it**.
2. Otherwise it recomputes the SHA256:
   - if it matches, it writes the marker and accepts the model;
   - if it does not match, it **deletes the model** and its marker and returns `false` so it gets downloaded again.
3. Verifications of the same file are serialized with one semaphore per path.

If a model exists but **cannot be loaded** (`WhisperModelLoadException` or `LoadWeightsFailedException`), `ModelIntegrity.LoadFailed` deletes the marker and throws `ErrorModelLoadFailed`. On the next use the hash is recomputed: if the file is damaged it is deleted and downloaded again; if not, the problem lies elsewhere (GPU, memory).

## Verification at startup

`MainViewModel.Start` runs `IModelVerifier.VerifyInstalledModelsAsync` in the background:

- It checks both installed models with `EnsureVerifiedAsync`. Usually reading the marker is enough.
- If one was damaged and deleted, it goes back to the first-run screen. It does not do so while a recording is in progress or being processed.
- If a model cannot be read (locked, no permissions), it warns with `NoticeModelVerificationFailed` and leaves it alone: it will be checked again before use.

## Updating a model

1. On the file's Hugging Face page, copy the file's **commit** and **SHA256**.
2. Change the revision and the hash **together**:
   - Whisper: `ModelRevision` and the `GgmlType.LargeV3Turbo` entry of `PinnedSha256` in `WhisperTranscriptionService.Download.cs`.
   - Qwen3: `ModelRevision` and `ModelSha256` in `SummarizationService.Download.cs`. If the file name changes, also `ModelDownloadUrl`, `ModelName` and `AppSettings.SummaryModelFileName`.
3. Update the sizes in the `FirstRun*ModelSize` and `Config*ModelName` texts in `Strings.cs`.
4. Users who already have the previous model will find that their marker does not match the new hash. The model will be rehashed, deleted, and the new one downloaded.

To support another Whisper size (`small`, `medium`…), it is enough to add its hash to `PinnedSha256`. `ParseModelSize` already recognizes the names.
