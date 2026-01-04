# ScrapLeaderboard

## Overview

**ScrapLeaderboard** is a unified Rust Oxide plugin that combines item deposit tracking with automated leaderboard management. It allows players to deposit specific items (default: Scrap) into a designated dropbox to compete for rewards.

This plugin replaces the standalone **DepositBox** and **UpdateLeaderboard** plugins, merging them into a single, optimized tool.

### Key Features

- **Real-Time Limit Enforcement:** Deposit limits are enforced via a live memory cache. Players cannot exceed the limit, even if the visual leaderboard has not updated yet.
- **Automated Leaderboard:** The admin command automatically generates a top-40 player list, handles pagination, and injects it directly into your `ServerInfo.json` config.
- **Item Removal:** Deposited items are logged and immediately removed from the game world to prevent clutter.
- **Reward Summaries:** Generates CSV/JSON summaries of all deposits, calculating percentage shares and reward payouts (e.g., Bitcoin/Sats) based on a configurable prize pool.

---

## Dependencies & Integrations

### Required
- **[ServerInfo](https://umod.org/plugins/server-info):** This plugin is required to visualize the leaderboard UI in-game.

### Optional / Recommended
- **[MonumentAddons](https://umod.org/plugins/monument-addons):** Highly recommended if you wish to spawn permanent, static deposit boxes at monuments (e.g., Outpost, Bandit Camp).
  > **Quick Start:** You can use our example **[bloodbank.json profile](https://github.com/orangemart/RustPlugins/tree/main/MonumentAddons)** to automatically deploy "Blood Bank" deposit stations at safe zones that work seamlessly with this leaderboard.

---

## Installation

1. Ensure **ServerInfo** is installed and configured on your server.
2. Upload `ScrapLeaderboard.cs` to your server's `oxide/plugins/` directory.
3. The plugin will handle the rest.
   > **Note:** If you are upgrading from **DepositBox**, this plugin automatically detects your existing `oxide/data/DepositBoxLog.json`, migrates the data to the new folder, and renames it to `ScrapLog.json` so no player history is lost.

---

## Permissions

**⚠️ Important:** The permission nodes use the new plugin name. You will need to re-grant these permissions if upgrading from DepositBox.

- `scrapleaderboard.place`
  - Allows a player to use the `/depositbox` command to receive and place a deployable dropbox.
- `scrapleaderboard.admin`
  - Allows access to the admin command `/scrapleaderboard`.

---

## Commands

### Player Commands

- `/depositbox`
  - Gives the player a specialized storage container (Drop Box) skinned for deposits.
  - *Requires `scrapleaderboard.place` permission.*

### Admin Commands

- `/scrapleaderboard [amount]`
  - **One-Step Update:** This single command performs three actions:
    1. Generates the deposit summary files (JSON/CSV).
    2. Updates the "Leaderboard" tab in `ServerInfo.json`.
    3. Reloads the ServerInfo plugin to apply changes immediately.
  - **[amount]** (Optional): Total prize pool size (default: 100,000). The plugin calculates exactly how much each player earned based on their percentage of total deposits.
  - *Requires `scrapleaderboard.admin` permission.*

---

## Configuration

The configuration file is located at `oxide/config/ScrapLeaderboard.json`.

**Default Configuration:**

```json
{
  "DepositBoxSkinID": 3616815672,
  "DepositItemID": -932201673,
  "MaxDepositLimit": 250000
}
```

- **DepositBoxSkinID:** The skin ID required for a container to function as a deposit box.
- **DepositItemID:** The Item ID of the item to accept (default is Scrap: `-932201673`).
- **MaxDepositLimit:** The maximum total amount a single player can deposit. This is enforced in real-time.

---

## Data & Output

All data is now stored in `oxide/data/ScrapLeaderboard/`.

### 1. The Log
- **File:** `ScrapLog.json`
- **Purpose:** Keeps a permanent record of every single deposit transaction.

### 2. Summary Reports
When you run the `/scrapleaderboard` command, the following files are generated/updated:

- **File:** `ScrapSummary.json`
  - Detailed breakdown of totals and percentages.
- **File:** `ScrapClaims.json`
  - Simplified list of `SteamID: RewardAmount` based on the prize pool.
- **File:** `ScrapSummary.csv`
  - A spreadsheet-compatible export useful for external auditing or publishing results.