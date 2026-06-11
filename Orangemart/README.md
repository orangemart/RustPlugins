# Orangemart (v0.7.2)

**Orangemart** is a [Rust plugin](https://umod.org/) that bridges your game server with the real-world Bitcoin Lightning Network. It allows players to buy in-game items, deposit/bank currency, send currency to other players (withdrawals), and purchase VIP status using instant Lightning payments via an LNbits backend.

## ⚡ New in v0.7.x
*   **Version 0.7.2 Bug Fixes:** Fixed a stack-merging desync where giving item rewards to a player with space (but with existing stacks) falsely printed an "Inventory full! dropped on ground" message.
*   **Automatic Lightning Address Lookup:** Players can now link their Steam account to their Lightning Address on the web (e.g., `theorangemart.com`). When withdrawing, the plugin automatically looks up their address using the new `ExternalApi` configuration.
*   **New `/bank` Command:** Automatically detects the player's currency, lookup address, and processes a deposit of all held currency to their Lightning wallet with a single command.
*   **Optional Address Argument:** The `/sendblood` command no longer requires typing a Lightning Address if the player's account is already linked on the server's lookup registry.
*   **Persistent Offline Queuing:** Invoices paid while a player is offline (or disconnected during an async transaction) are safely queued and delivered automatically when they reconnect.
*   **Descriptive Outbound Errors:** Replaced blind silent failures and refunds with robust error reports returned directly to the player when outbound routing fails.
*   **Real-Time Exchange Rates:** Supports real-time USD/Fiat to SATS conversion for dynamic automated pricing based on live exchange rates.
*   **Improved Formatting & Admin Webhooks:** Added support for native BTC `₿` formatting in messages/Discord embeds and a separate admin notification webhook.

-----

## Features
*   **Real-Time Deposits (`/buyblood`):** Players generate a QR code (linked to Discord) to buy in-game currency. Payments are detected instantly via WebSockets.
*   **Withdrawals (`/sendblood`):** Players can "burn" in-game items to send real Sats to any Lightning Address (e.g., Wallet of Satoshi, Strike, CashApp).
*   **Banking (`/bank`):** Instant withdrawal of all held currency in inventory.
*   **VIP Automation:** Purchase VIP status / permissions automatically with Bitcoin.
*   **Discord Integration:** Sends beautiful embed invoices to a designated Discord channel.
*   **Anti-Abuse:** Configurable cooldowns, per-player transaction limits, and pending invoice caps.

-----

## Commands

### Player Commands
*   **`/buyblood <amount>`**
    Generates a Lightning invoice to purchase in-game currency.
    * *Example:* `/buyblood 100`

*   **`/sendblood <amount> [lightning_address]`**
    Destroys in-game currency and sends real Bitcoin to the specified address. The `lightning_address` is optional if the server has an automatic API lookup configured and the player has linked their address.
    * *Example:* `/sendblood 50 user@walletofsatoshi.com` or `/sendblood 50`

*   **`/bank`**
    Automatically withdraws all held currency in the player's inventory to their linked Lightning Address.
    * *Example:* `/bank`

*   **`/buyvip`**
    Generates an invoice to purchase VIP status (runs the configured console command upon success).

-----

## Configuration

The configuration file allows you to set connection details, pricing, and security limits.

### Default Configuration (`oxide/config/Orangemart.json`)

```json
{
  "Commands": {
    "BuyCurrencyCommandName": "buyblood",
    "SendCurrencyCommandName": "sendblood",
    "BankCommandName": "bank",
    "BuyVipCommandName": "buyvip"
  },
  "ExternalApi": {
    "AddressLookupApiUrl": "https://yourwalletsite.com/api/server/resolve-address",
    "AddressLookupApiKey": "your_wallet_api_key"
  },
  "CurrencySettings": {
    "CurrencyItemID": 1776460938,
    "CurrencyName": "blood",
    "CurrencySkinID": 0,
    "PricePerCurrencyUnit": 1.0,
    "CurrencyPriceCurrency": "SATS",
    "SatsPerCurrencyUnit": 1,
    "MaxPurchaseAmount": 10000,
    "MaxSendAmount": 10000,
    "CommandCooldownSeconds": 0,
    "MaxPendingInvoicesPerPlayer": 1
  },
  "Discord": {
    "DiscordChannelName": "mart",
    "DiscordWebhookUrl": "https://discord.com/api/webhooks/your_webhook_url",
    "AdminDiscordWebhookUrl": ""
  },
  "InvoiceSettings": {
    "BlacklistedDomains": [
      "example.com",
      "blacklisted.net"
    ],
    "WhitelistedDomains": [],
    "CheckIntervalSeconds": 10,
    "InvoiceTimeoutSeconds": 300,
    "LNbitsApiKey": "your-lnbits-admin-api-key",
    "LNbitsBaseUrl": "https://your-lnbits-instance.com",
    "MaxRetries": 25,
    "UseWebSockets": true,
    "WebSocketReconnectDelay": 5
  },
  "VIPSettings": {
    "VipCommand": "oxide.usergroup add {steamid} vip",
    "VipPrice": 1000.0,
    "VipPriceCurrency": "SATS"
  }
}
```

### Key Settings Explained

#### 🔗 External API Settings (New)
*   **`AddressLookupApiUrl`**: The endpoint used to resolve a player's Steam ID to their Lightning Address (defaults to the Orangemart resolution api).
*   **`AddressLookupApiKey`**: The API token authorization key for the lookup endpoint.

#### 🛡️ Protection Settings
*   **`MaxPurchaseAmount`**: The maximum amount of items a player can buy in one go.
*   **`MaxSendAmount`**: The maximum amount a player can withdraw/send in one go.
*   **`CommandCooldownSeconds`**: Time (in seconds) a player must wait between commands. Set to `0` to disable.
*   **`MaxPendingInvoicesPerPlayer`**: Prevents players from spamming the server with unpaid invoices.

#### ⚡ Invoice Settings
*   **`UseWebSockets`**: Set to `true` for instant payment detection. If `false`, it falls back to slower polling.
*   **`LNbitsBaseUrl`**: Your LNbits server URL (e.g., `https://legend.lnbits.com`).
*   **`LNbitsApiKey`**: The **Admin Key** from your LNbits wallet.

#### 👑 VIP Settings
*   **`VipCommand`**: The console command to run when payment is successful. Supports placeholders:
    *   `{player}` - Player Name
    *   `{steamid}` - Steam ID (UserID)
    *   `{userid}` - User ID (Steam64)

-----

## Installation

1.  **Prerequisites:** You must have an [LNbits](https://github.com/lnbits/lnbits) wallet instance running (or use a hosted one).
2.  **Download:** Place `Orangemart.cs` into your `oxide/plugins` folder.
3.  **Config:** Edit `oxide/config/Orangemart.json` and add your **LNbits API Key**, **Discord Webhook URL**, and optional **External API keys**.
4.  **Reload:** Run `o.reload Orangemart`.

-----

## Permissions

*   **`orangemart.buycurrency`** - Allows players to use `/buyblood`
*   **`orangemart.sendcurrency`** - Allows players to use `/sendblood` and `/bank`
*   **`orangemart.buyvip`** - Allows players to use `/buyvip`

-----

## Troubleshooting

*   **Invoices not appearing in Discord?**
    Check that your `DiscordWebhookUrl` is correct and that the plugin has loaded without errors in the server console.
*   **Payments not registering instantly?**
    Ensure `UseWebSockets` is set to `true` and that your server can connect to your LNbits instance via port 443 (HTTPS/WSS).
*   **"Inventory Full" messages?**
    If a player's inventory is genuinely full when they pay for items, they will drop at their feet on the ground.
