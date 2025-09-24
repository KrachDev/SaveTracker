# 🎉 **SaveTracker v1.0** – *Initial Release*

**SaveTracker** is a Playnite plugin that *automatically detects and tracks* your game's save files. 
__utilising ETW for seemless tracking

This first release brings a smart, streamlined way to monitor, summarize, and back up save data after every play session.

---

## 🧠 **Key Features**

- 🎮 **Automatic Save Tracking**
  - Starts tracking new/modified save files when the plugin is toggled **on**.
  - Keeps overhead low by only monitoring during game sessions.

- 📋 **Post-Game Save Summary**
  - When the game closes, SaveTracker:
    - Displays a **notification** with tracked file changes.
    - Saves a detailed list to `GameFiles.json` inside the game directory.

- ☁️ **Cloud Backup (via Rclone)**
  - Seamless integration with **Rclone** to sync your saves to the cloud.
  - Works with multiple providers (Google Drive, Dropbox, OneDrive, etc.).
  - Automatic Rclone setup and config.

- ⚙️ **Customizable**
    - Enable/disable auto-tracking.
    - Choose whether to sync, and select the target provider.
    - Fine-tune what gets logged or ignored.

---

## 📦 **Installation**

To install:

1. Download the latest `.pext` file. and Double click it! or:
2. In Playnite, go to **Extensions → Add Extension**.
3. Select the `.pext` file and restart if needed.

---
