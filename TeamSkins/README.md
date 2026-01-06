# Team Skins

**Team Skins** is a complete replacement for the standard Skins plugin, designed for Rust (Oxide/uMod). It offers a modern, team-aware solution that handles "special" items correctly.

This version (v2.0.0) is a major rewrite that functions independently of the original `Skins` plugin and introduces the "Ghost Adoption" mechanic to solve long-standing issues with redirect skins like the Space Suit or Nomad Suit.

## Features

* **Skins Replacement:** A full alternative to existing skin plugins. No bridges required—this handles all UI and logic itself.
* **Team Sharing:** Automatically detects teammates and pools their owned skins. If your teammate owns a glowing AK-47, you can use it while you are online together.
* **Ghost Adoption (New):** Solves the "Redirect Item" problem.
    * Instead of trying to "paint" an item, the plugin lets you "adopt" the preview ghost entity directly from the skin box.
    * This guarantees that **Space Suits**, **Knight Armor**, and **Nomad Suits** are created correctly with their unique stats and properties.
    * Supports **Reverse Skinning** (converting a Space Suit back to a Hazmat Suit).
* **Hybrid Cache:** Combines Steam Workshop definitions with Rust's internal item definitions, ensuring DLC items and Workshop skins appear in the same menu.
* **Configurable:** Full control over commands, UI capacity, and server-defined extra skins.

## Dependencies

* [PlayerDLCAPI](https://umod.org/plugins/player-dlc-api) (by k1lly0u) - Required to verify ownership of skins via Steam.

## Permissions

* `teamskins.use` -- Required for players to open the menu and use the skinning features.

## Installation

1.  **Remove** the original `Skins` plugin (if installed) to prevent command conflicts.
2.  Download `TeamSkins.cs` and place it in your `oxide/plugins` folder.
3.  Ensure `PlayerDLCAPI` is installed and loaded.
4.  The plugin will automatically build its cache on the first run.
5.  Grant the permission to your players: `oxide.grant group default teamskins.use`

## Usage

* **Open Menu:** Type `/skin` (or configured command) while holding an item or looking at one.
* **Drag & Drop:** Open the menu and drag an item into the container.
* **Apply Skin:**
    * **Click** a skin icon to instantly swap your item.
    * **Drag** a skin icon into your inventory to "adopt" it.
    * *Note: Creating a special item (e.g., Space Suit) will destroy the original item and replace it with the new entity. Condition, ammo, and attachments are preserved.*

## Configuration

A config file is generated at `oxide/config/TeamSkins.json`.

```json
{
  "Commands": [
    "skin",
    "skins",
    "sb"
  ],
  "Container Panel Name": "generic",
  "Container Capacity": 36,
  "Extra Skins": [
    {
      "Item Shortname": "hoodie",
      "Permission": "teamskins.admin",
      "Skins": [
        3492377614
      ]
    }
  ]
}
```

* **Commands:** List of chat commands to open the menu.
* **Extra Skins:** Manually add skins to the menu (useful for custom server skins). You can optionally lock these behind a permission.

## Credits

* **Orangemart** - Logic, Design, and v2.0 Rewrite.
* **misticos** - Author of the original *Skins* plugin. This project builds upon the UI logic and concepts established by their work.
* **k1lly0u** - Author of *PlayerDLCAPI*.