# Team Skins

**Team Skins** is a bridge plugin for Rust (Oxide/uMod) that allows players to share their owned item skins with their teammates. 

If Player A owns a specific glowing AK-47 skin, and they are in a team with Player B, Player B will be able to see and apply that skin using the `Skins` menu, just as if they owned it themselves.

## Features

* **Team Sharing:** Automatically detects teammates and pools their skin collections together.
* **Workshop Support:** Fully supports thousands of Steam Workshop skins, not just built-in DLCs.
* **Dynamic Caching:**
    * Builds a lightweight cache of skin IDs on server start to ensure performance.
    * Automatically clears player caches when they join, leave, or disband a team, ensuring the menu is always up-to-date.
* **Zero Configuration:** Uses Rust's native Team system. No permissions or config files required.

## Dependencies

This plugin acts as a bridge between two other plugins. Both must be installed for `Team Skins` to function.

* [Skins](https://umod.org/plugins/skins) (by misticos) - Provides the UI and skin application logic.
* [PlayerDLCAPI](https://umod.org/plugins/player-dlc-api) (by k1lly0u) - Verifies ownership of skins via Steam.

## Installation

1.  Download the `TeamSkins.cs` file.
2.  Place it into your server's `oxide/plugins` folder.
3.  Ensure both **Skins** and **PlayerDLCAPI** are loaded.
4.  The plugin will automatically initialize and build a skin cache from Steam definitions.

## How It Works

1.  **The Trigger:** When a player opens the Skin Box (e.g., puts an item in), the `Skins` plugin asks for a list of available skins.
2.  **The Check:** `Team Skins` intercepts this request and checks if the player is in a team.
3.  **The Verification:** It iterates through all **online** teammates and asks `PlayerDLCAPI`: *"Does this teammate own any skins for this specific item?"*
4.  **The Result:** Any verified skins owned by teammates are added to the player's list and displayed in the UI.

### Important Notes

* **Online Requirement:** Teammates must be **ONLINE** to share their skins. Steam's inventory API cannot verify the entitlements of offline players.
* **Cache Invalidation:** The plugin automatically forces the `Skins` plugin to "forget" a player's data if their team status changes (e.g., they get kicked or leave a team). This ensures they lose access to shared skins immediately.

## Developer & API

**Hooks Used:**
* `OnSkinsFetch` (from Skins) - Used to inject shared skins into the list.

**Commands:**
* No commands are required for general use.
* Debug: `TeamSkins` will output cache build status to the server console on startup.

## Credits

* **Orangemart** - Logic and Design
* **misticos** - Author of the original *Skins* plugin
* **k1lly0u** - Author of *PlayerDLCAPI*