# Miner (Fridge Scrap Generator)

Custom-skinned **fridges** generate **scrap** while sufficiently powered. Includes a **/miner.craft** command with configurable costs, per-player limits, VIP limits, localization (Lang), and admin utilities (reload, scan, wipe).&#x20;

---

## Features

* Turn selected fridges (by prefab + **skin ID**) into passive **scrap miners**.
* Produces a fixed **ScrapPerTick** every **IntervalMinutes**, only if **RequiredPower** is met.
* **/miner.craft** gives a skinned “Miner” fridge for a configurable **CraftCost** (items/amounts).
* **Per-player craft limits**, with a separate cap for **VIPs**.
* **Permissions** for crafting and VIP.
* **Lang-based** chat messages (easy to translate).
* Admin tools: **/fsg.reload**, **/fsg.scan**, and **miner\_wipe** (chat or console).
* Production logic is robust across Rust/Oxide versions (input power read is version-tolerant).
* Safe scrap insertion into the fridge inventory (works around vanilla scrap filter).

---

## Requirements

* Rust server with **uMod/Oxide**.
* A fridge skin you want to designate as a “Miner” (default skin ID below).
* Sufficient electrical power for each Miner.

---

## Installation

1. Place `Miner.cs` into your server’s `oxide/plugins/` folder.
2. Start/reload the server to generate the default config & lang files.
3. Adjust the configuration (see below), then **/fsg.reload** (admin) to apply.

---

## Configuration

A config file is created at `oxide/config/Miner.json`. Key options and defaults:

```json
{
  "FridgePrefabShortname": "fridge.deployed",
  "TargetSkinId": 1375523896,
  "RequiredPower": 100,
  "InputCompensationWatts": 5,
  "ScrapPerTick": 21,
  "IntervalMinutes": 60.0,
  "MaxFridgeSlotsToUse": 48,

  "CraftEnabled": true,
  "CraftPermissionRequired": false,
  "CraftPermission": "miner.craft",
  "MinerItemShortname": "fridge",
  "MinerItemDisplayName": "Miner",
  "CraftCost": {
    "scrap": 500,
    "metal.fragments": 2500,
    "gears": 10
  },

  "CraftLimitEnabled": true,
  "CraftMaxPerPlayer": 1,

  "VipPermission": "miner.vip",
  "VipCraftMaxPerPlayer": 2,

  "LogDebug": false
}
```

**Notes & tips**

* **Targeting the fridge:**

  * Only fridges with `ShortPrefabName == FridgePrefabShortname` **and** `skinID == TargetSkinId` are treated as Miners.
  * Default skin: **1375523896**.
* **Power:**

  * A Miner only produces if its **adjusted** input power is at least **RequiredPower**.
  * Adjusted power = server-reported input + **InputCompensationWatts** (compensates entity self-draw).
* **Production:**

  * Every **IntervalMinutes**, the plugin attempts to add **ScrapPerTick** scrap into the Miner’s fridge inventory.
  * It tops up partial stacks first, then creates new stacks while respecting **MaxFridgeSlotsToUse**.
* **Crafting:**

  * **CraftCost** uses **item shortnames** (e.g., `metal.fragments`, `gears`).
  * If a shortname is invalid, the “not enough” message will label it **(unknown item)** and crafting will fail until fixed.
  * **MinerItemShortname** is the item a player receives (default `fridge`) with the configured **TargetSkinId** and **MinerItemDisplayName**.
* **Limits:**

  * Enable **CraftLimitEnabled** to cap total crafts per player.
  * **VipPermission** holders use **VipCraftMaxPerPlayer** instead of **CraftMaxPerPlayer** (0 = unlimited).
* **Debugging:**

  * Set **LogDebug** to `true` for verbose console logs (power readings, insert behavior, etc.).

---

## Localization (Lang)

Default messages are registered under the plugin name. To customize or translate:

* Copy the generated language file (e.g., `oxide/lang/en/Miner.json`) and edit messages.
* Keys include:

  * `Craft.Disabled`, `Craft.NoPermission`, `Craft.NotEnoughHeader`, `Craft.NotEnoughLine`,
    `Craft.Success`, `Craft.SuccessRemaining`, `Craft.LimitReached`,
    `Admin.WipeDone`, `Admin.Reloaded`, `Scan.Result`, `Error.CreateItem`.

Messages like **Craft.NotEnoughLine** are automatically formatted with missing item and amount.

---

## Permissions

* **`miner.craft`** – Allows using **/miner.craft** if `CraftPermissionRequired` is `true`.
  *(Registered automatically when set in config.)*
* **`miner.vip`** – Grants the VIP craft limit.
  *(Registered automatically when set in config.)*

Use standard Oxide permission commands, e.g.:

```
oxide.grant user <steamid> miner.craft
oxide.grant group vip miner.vip
```

---

## Commands

### Player

* **`/miner.craft`**
  Craft one Miner (skinned fridge) by paying **CraftCost**.

  * Enforces per-player limit (and VIP limit if applicable).
  * Shows remaining crafts (if any) and power requirement.

### Admin

* **`/fsg.reload`** *(admin only)*
  Reloads config and restarts the production timer (safe live update).
* **`/fsg.scan`** *(admin only)*
  Reports the number of targeted Miners currently present on the map.
* **`/miner_wipe`** *(admin only via chat)*
  Resets all **per-player craft counts** to zero.
* **`miner_wipe`** *(RCON / server console)*
  Same as above (console variant).

---

## How it Works (under the hood)

* **Discovery:** On server init and on entity spawn, the plugin locates fridges matching **FridgePrefabShortname** and **TargetSkinId**, tracks them, and sets their display name to **MinerItemDisplayName**.
* **Power read (version-tolerant):** It tries `GetCurrentEnergy()`, falls back to passthrough or `IsPowered()`. Then **adds** `InputCompensationWatts` and compares to **RequiredPower** before producing.
* **Scrap insertion:**

  * Allows scrap into tracked fridge containers via `CanAcceptItem` hook (vanilla fridges normally block scrap).
  * Fills partial stacks first, then creates new stacks. If standard insertion fails (e.g., filters), it **safely force-inserts** as a fallback to keep production reliable.
* **Persistence:** Per-player craft counts are stored in `oxide/data/Miner.Craft.json`. Admin wipe clears this file’s counters only.

---

## Examples

* **Make Miners faster but weaker:**

  * `ScrapPerTick: 10`, `IntervalMinutes: 15`, `RequiredPower: 50`
* **Heavier late-game Miners:**

  * `ScrapPerTick: 50`, `IntervalMinutes: 60`, `RequiredPower: 150`, `CraftCost`: add high-tier components.

---

## Troubleshooting

* **“Not enough resources” mentions an unknown item:**
  Double-check the **CraftCost** item shortnames. Use Rust’s exact shortnames (e.g., `techparts` vs `tech.trash`—ensure the correct one for your server build).
* **Miners not producing:**

  * Verify the entity is the **correct fridge skin** and **prefab shortname**.
  * Confirm **adjusted input power ≥ RequiredPower** (consider raising `InputCompensationWatts` if your power network has self-draw quirks).
  * Check **MaxFridgeSlotsToUse** and storage capacity; a completely full container cannot accept new stacks.
* **Lang message tweaks not applying:**
  Ensure you edited the correct `oxide/lang/<language>/Miner.json` and that your server language matches. Use **/fsg.reload** after edits.

---

## Compatibility Notes

* Designed to be resilient to different Oxide builds (two `CanAcceptItem` hook signatures are handled).
* Production runs on a timer; changing **IntervalMinutes** or **ScrapPerTick** takes effect after **/fsg.reload**.

---

## Changelog (summary)

* **1.8.0**

  * VIP craft limit & separate permission.
  * Per-player craft limit toggle.
  * Configurable **CraftCost**, **RequiredPower**, **IntervalMinutes**, **ScrapPerTick**, **MaxFridgeSlotsToUse**.
  * Language keys for all chat outputs.
  * Admin **reload/scan/wipe** commands.
  * Safer scrap insertion and inventory discovery improvements.&#x20;

---

## Credits

* **Author:** Orangemart
* **Plugin Name:** Miner 
* **License:** Use at your own risk; please credit when forking or redistributing.&#x20;

---

## Support & Contributions

* Found a bug or have a feature request? Open an issue or PR in your plugins repo, or contact the server admin team.
* If you localize the Lang file, consider sharing your translation with the community!
