# Vote Restart

**Vote Restart** is an Oxide/uMod plugin for Rust that empowers your players to trigger a graceful server restart via a voting system. 

This is incredibly useful for community servers during unexpected Facepunch patches or Oxide updates when server administrators might be asleep or offline. Instead of waiting for an admin to manually trigger the update, the community can vote to safely restart the server, automatically applying pending updates (depending on your host's startup scripts).

## Features

* **Graceful Shutdown:** Uses Rust's native `restart` command to ensure the server saves properly and players get the standard on-screen countdown warning.
* **Dynamic Resolution:** If the required "Yes" percentage is met before the voting timer expires, the restart triggers immediately.
* **Abuse Prevention:** 
  * Configurable cooldowns between failed votes to prevent chat spam.
  * Minimum online player requirement (prevents a single player on an empty server from constantly restarting it).
* **Fully Configurable:** Easily adjust the required vote percentage, countdown timers, and cooldowns.

## Installation

1. Download `VoteRestart.cs`.
2. Place the file into your `oxide/plugins/` directory.
3. The plugin will compile automatically and generate a default configuration file.

## Commands

* `/voterestart` — Initiates a server restart vote.
* `/vote yes` (or `/vote y`) — Casts a vote in favor of restarting.
* `/vote no` (or `/vote n`) — Casts a vote against restarting.

*Note: The player who initiates the vote is automatically counted as a "Yes".*

## Configuration

The configuration file can be found at `oxide/config/VoteRestart.json`. 

```json
{
  "Required Yes Percentage (0.0 to 1.0)": 0.8,
  "Vote Duration (Seconds)": 60.0,
  "Cooldown Between Votes (Seconds)": 300.0,
  "Minimum Players Online to Vote": 2,
  "Restart Countdown Timer (Seconds)": 10
}
```

### Configuration Details

* **Required Yes Percentage:** The fraction of *currently online* players that must vote "Yes" for the vote to pass. `0.8` means **80%**. `1.0` means **100%**.
* **Vote Duration:** How long (in seconds) the poll remains open before tallying the final results.
* **Cooldown Between Votes:** How long (in seconds) players must wait after a vote concludes before anyone can type `/voterestart` again. Default is 5 minutes.
* **Minimum Players Online to Vote:** The server must have at least this many players online to initiate a vote. 
* **Restart Countdown Timer:** The delay (in seconds) between the vote passing and the server actually shutting down. This allows players a brief window to run to a safe zone.

## Permissions

Currently, there are no permissions required. Any active player can initiate or participate in a vote, provided the minimum player count and cooldown requirements are met.