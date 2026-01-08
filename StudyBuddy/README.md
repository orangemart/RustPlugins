# StudyBuddy

**Author:** Orangemart  
**Version:** 1.0.0  

## Overview

**StudyBuddy** is a lightweight, high-performance blueprint sharing plugin for Rust. It allows online teammates to "copy your homework" in real-time.

When a player researches an item or unlocks a node on the Tech Tree, that blueprint is instantly unlocked for all **currently online** teammates. 

### Why "Online Only"?
StudyBuddy is built for maximum server performance. By strictly limiting sharing to online players, the plugin operates entirely in system RAM (memory). It performs **zero** hard disk reads or writes during gameplay, ensuring that even if a clan spams the Tech Tree, your server will never stutter or lag.

## Features

* **Zero Lag:** No database files, no I/O overhead.
* **Live Sharing:** Works instantly with the Research Table and Tech Tree.
* **Team UI Integrated:** Automatically detects teammates via the native Rust Team UI.
* **Permissions:** Control exactly who is smart enough to let others copy their homework.

## Permissions

* `studybuddy.use` -- Allows a player to share their blueprints. 
    * *Note: Only the "Sharer" needs this permission. Teammates do not need permission to receive blueprints.*

**Example:**
```bash
o.grant group default studybuddy.use
```

## Configuration

The configuration file can be found in `oxide/config/StudyBuddy.json`.

```json
{
  "Share Tech Tree Blueprints": true,
  "Items Blocked from Sharing": [
    "lmg.m249",
    "explosive.timed"
  ]
}
```

* **Share Tech Tree Blueprints:** If set to `false`, sharing only happens at the Research Table.
* **Items Blocked from Sharing:** A list of item shortnames that will never be shared, even if the player has permission.

## Installation

1.  Download `StudyBuddy.cs`.
2.  Place the file into your server's `oxide/plugins` folder.
3.  The plugin will load automatically.
4.  Grant the `studybuddy.use` permission to your default group (or VIP groups).

## Developer Notes

This plugin intentionally lacks "offline sharing" and "database persistence" to prioritize server FPS. It relies on the native Rust `RelationshipManager` to find teammates and direct RPC calls to update client UIs immediately.

## Credits

* **c_creep** - Original author of the *Blueprint Share* plugin, which provided the foundation and inspiration for this lightweight rewrite.