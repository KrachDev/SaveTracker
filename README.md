# 🎉 **SaveTracker** – *A Playnite Cloud-Saving Solution*

**SaveTracker** is a Playnite plugin that *automatically detects and tracks* your game's save files,  
utilizing **ETW (Event Tracing for Windows)** for seamless, low-overhead monitoring.

This first release introduces a smart and streamlined way to monitor, summarize, and back up save data after every play session.

---

## 🧠 **Key Features**

- 🎮 **Automatic Save Tracking**
  - Starts tracking new or modified save files when the plugin is toggled **on**.
  - Keeps overhead low by only monitoring during game sessions.

- 📋 **Post-Game Save Summary**
  - When the game closes, SaveTracker:
    - Displays a **notification** with tracked file changes.
    - Saves a detailed log to `GameFiles.json` inside the game directory.

- ☁️ **Cloud Backup (via Rclone)**
  - Seamless integration with **Rclone** to sync your saves to the cloud.
  - Supports multiple providers: Google Drive, Dropbox, OneDrive, and more.
  - Automatically sets up and configures Rclone.

- ⚙️ **Customizable**
  - Enable or disable auto-tracking.
  - Choose whether to sync and select your preferred cloud provider.
  - Fine-tune what gets logged or ignored with filtering options.

---

## 📦 **Installation**

To install:

1. Download the latest `.pext` file and double-click it, **or**:
2. In Playnite, go to **Extensions → Add Extension**.
3. Select the `.pext` file and restart Playnite if needed.

Alternatively, install it directly from the **Generic Extensions Store** inside Playnite.
