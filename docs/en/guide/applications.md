# Applications

The **Applications** screen lets you choose which programs, besides Teams, appear in the sidebar so they can be recorded.

## Adding an application

1. Open the application you want to record and make it **play audio**: join a call or play a video.
2. In **Applications**, the **Detected processes with audio** section lists the processes that have an audio session right now. It refreshes on its own every 2 seconds while the screen is open, and also with **Refresh**. Applications that are already watched are not listed.
3. Click **Add**. The application moves to **Added for listening** and appears in the sidebar.

The name shown is the executable's description (for example, "Zoom Meetings") or, if it has none, the process name. That name is also the name of its transcripts folder.

Added applications are saved in the settings with their process name (for example `zoom` or `chrome`) and are watched just like Teams.

> [!TIP]
> For Google Meet or other meetings in the browser, add the browser (Chrome, Edge…). Keep in mind that **all** of the browser's audio is recorded, not just the meeting tab: close other tabs that play sound.

## Removing an application

Click **Remove** under **Added for listening**. If the application has saved transcripts, it cannot be removed: delete them from the history first. Teams is built in and cannot be removed.

## How the process is chosen

Many applications run several processes with the same name (Teams, browsers). The application:

1. Finds the processes with that name and keeps the root of the tree, the one whose parent is not another of them.
2. If one of those processes has an active audio session, prefers that one.
3. Records that process **and all its child processes**, which is usually where call audio and browser tabs live.
