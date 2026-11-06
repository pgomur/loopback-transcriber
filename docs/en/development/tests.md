# Tests

`LoopbackTranscriber.Tests` uses **xUnit 2.9.3** and targets `net10.0-windows` with WPF, because the ViewModels depend on WPF types such as `DispatcherTimer`. Tests run **without a window, audio devices, models or worker processes**: everything external is replaced by test doubles.

## Running

| Command | Runs |
|---|---|
| `dotnet test LoopbackTranscriber.slnx -c Release` | All the tests. |
| `dotnet test LoopbackTranscriber.slnx --filter Category=Unit` | Only the unit tests. |
| `dotnet test LoopbackTranscriber.slnx --filter Category=Integration` | Only the integration tests. |
| `dotnet test LoopbackTranscriber.slnx --filter "FullyQualifiedName~RecordingWorkflow"` | The tests of one class, by name. |

In Visual Studio: *Test → Test Explorer → Run All*. To group by category, use *Group By → Traits*.

## Layout

| Folder or file | Contents |
|---|---|
| `GlobalUsings.cs` | Common namespaces and the `TranscriptSegment` alias. |
| `Infrastructure\` | Utilities shared by the tests. |
| `Fakes\` | Test doubles for the application's interfaces. |
| `Models\` | One file per type in `Models\`. |
| `Services\` | One file per service. |
| `ViewModels\` | One file per ViewModel. |

Each test class is named after the class it tests plus `Tests`. Each test follows the `Situation_ExpectedResult` shape, for example `WhileRecording_No_ExitsAtOnce_KeepingTheAudioToProcessLater`.

### Categories

Each class has a `[Trait(TestCategory.Name, …)]` trait:

| Category | Meaning |
|---|---|
| `Unit` | Pure logic: no disk, no clock, no threads. Runs in milliseconds. |
| `Integration` | Uses a temporary folder, real WAV files or real timing. Still fast and isolated. |

## Infrastructure

| Type | Used for |
|---|---|
| `TestEnvironment` | `[ModuleInitializer]` that runs before any test. Creates `%TEMP%\LoopbackTranscriber.Tests\<pid>`, redirects `AppLog.LogPath` there so nothing is written to the real log, and sets the language to Spanish. |
| `TempDirectory` | A temporary folder of each test's own. Deleted when disposed. |
| `TestSettings` | `AppSettings` with every folder inside the temporary one. `CreateModelFiles` creates fake 1-byte models so the application believes they are installed. |
| `WavFile` | Writes and reads test WAVs: tones, silence, PCM or float, deliberately broken headers, duration and format. |
| `Eventually.Until(condition, reason)` | Waits without blocking, checking the condition every 10 ms, for up to 5 s by default. If it is not met, fails with the reason. |
| `CollectingProgress<T>` | Synchronous `IProgress<T>` that stores the values. |
| `InterfaceLanguage.Use(lang)` | Changes the interface language and restores it when disposed. |
| `InterfaceLanguageTestGroup` | xUnit collection **without parallelization**. The language is global state, so tests that change it go in it. |
| `RecordingRig` | Assembles the whole recording flow with doubles: `CaptureCoordinator`, `RecordingWorkflow`, `RecordingProcessor`, `RecordingViewModel`, the failed recordings store and Teams as the application. Includes helpers such as `StartRecordingAsync`, `WhisperSays` and `LeaveOrphan`. |
| `ShellRig` | `MainViewModel` with all its children, on top of a `RecordingRig`. |

## Test doubles

| Double | Replaces | What it allows |
|---|---|---|
| `FakeProcess` | `IProcessCapture` | Writing a test WAV, delaying the start (`StartGate`), counting starts and setting the peak. |
| `FakeDevice` | `IDeviceCapture` | Returning device tracks with any peak you like. |
| `FakeMic` | `IMicCapture` | Simulating a microphone that starts or not (`StartSucceeds`). |
| `FakeWhisper` | `IWhisperTranscriber` | Returning segments by file name and a detected language, holding the transcription (`Gate`) and recording the requests. |
| `FakeAudio` | `IAudioPreparer` | Duration, activity and level maps, and noise gate failures. |
| `FakeSettingsStore` | `ISettingsStore` | In-memory settings. Each read returns a **copy**, like the real service. Counts updates. |
| `FakeDialogs` | `IDialogService` | Fixed answers to Yes/No and Yes/No/Cancel. Records errors and info messages. |
| `FakeTargetWatcher` | `ITargetWatcher` | Marking applications as open or closed (`SetRunning`). |
| `FakeCustomApps` | `ICustomAppsService` | Adding and removing applications. |
| `FakeSummarizer` | `ISummarizer` | A fixed summary and a call counter. |
| `FakeLiveHost` | `IRecordingHost` | A simulated recording for the floating window and the tray: delaying or failing the start, and reacting to the stop (`OnStop`). |
| `FakePageFactory` | `IPageFactory` | Counting the pages created. |
| `FakeModelVerifier` | `IModelVerifier` | Result or exception of the model verification. |

## Guidelines

- **No test touches the user's real state**: not `settings.json`, `%LocalAppData%`, the log, nor real processes or devices.
- Services that save to disk take their path through the constructor, such as `SettingsService(notices, settingsPath)` or `FailedRecordingStore(rootFolder)`. Tests pass them a path inside their `TempDirectory`.
- To wait for something asynchronous, use `Eventually.Until`, not `Task.Delay` with a fixed time. To control when an operation finishes, use a `TaskCompletionSource` (the doubles' `Gate`s).
- Messages between ViewModels are tested with a `WeakReferenceMessenger` of each test's own, not `WeakReferenceMessenger.Default`.
- Expected texts are obtained with `Strings.Instance.Get(...)` or `Format(...)` instead of being written by hand, except when the translation itself is what is being tested.
- Do not use your real name in test data: the tests use `pgomur`.
