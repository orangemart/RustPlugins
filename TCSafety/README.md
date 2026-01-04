# TCSafety

**TCSafety** is a Rust plugin designed to enforce secure placement rules for Tool Cupboards (TCs) and improve quality of life for players. It prevents players from placing TCs on weak or invalid surfaces and automatically secures them with a key lock upon deployment.

## Features

* **Foundation Enforcement:** Ensures Tool Cupboards are placed strictly on foundations (preventing placement on floors/ceilings where they might be easily undermined).
* **Anti-Twig Protection:** Blocks placement on Twig grade foundations to prevent easy raiding or griefing.
* **Auto-Lock:** Automatically deploys a Key Lock and locks it immediately after the TC is placed.
* **Smart Refund:** If a placement is blocked due to rules, the TC item is refunded to the player's inventory automatically.

## Permissions

This plugin currently has no permissions. It applies globally to all players to ensure server-wide consistency.

## Configuration

The settings can be configured in the `TCSafety.json` file under the `oxide/config` directory.

### Default Configuration
```json
{
  "RequireFoundation": true,
  "BanTwigFoundation": true,
  "AutoLock": true,
  "RefundItemOnBlock": true
}
```

* `RequireFoundation` (true/false): If true, TCs can only be placed on Foundation blocks.
* `BanTwigFoundation` (true/false): If true, TCs cannot be placed on Twig. Players must upgrade the foundation to Wood or higher first.
* `AutoLock` (true/false): If true, a key lock is spawned and attached instantly.
* `RefundItemOnBlock` (true/false): If true, gives the TC back to the player if placement failed validation.

## Localization

The default messages are in English. You can modify them in the `oxide/lang/en/TCSafety.json` file.

```json
{
  "Error_NotFoundation": "Tool Cupboards must be placed on a foundation.",
  "Error_TwigFound": "Tool Cupboards cannot be placed on Twig foundations. Upgrade the foundation first.",
  "Error_Generic": "Invalid Tool Cupboard placement."
}
```

## Installation

1.  Download `TCSafety.cs`.
2.  Place the file into your server's `oxide/plugins` directory.
3.  The plugin will load automatically.
4.  (Optional) Edit `oxide/config/TCSafety.json` to customize settings.

## Developer & Credits

* **Author:** Orangemart
* **Version:** 1.0.1