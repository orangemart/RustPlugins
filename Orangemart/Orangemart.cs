using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Libraries;

namespace Oxide.Plugins
{
    [Info("Orangemart", "RustySats Orangemart", "0.6.2")]
    [Description("Allows players to buy and sell in-game units and VIP status using Bitcoin Lightning Network payments via LNbits with Fiat/BTC pricing and comprehensive protection features")]
    public class Orangemart : CovalencePlugin
    {
        // Configuration sections and keys
        private static class ConfigSections
        {
            public const string Commands = "Commands";
            public const string CurrencySettings = "CurrencySettings";
            public const string Discord = "Discord";
            public const string InvoiceSettings = "InvoiceSettings";
            public const string VIPSettings = "VIPSettings";
        }

        private static class ConfigKeys
        {
            // Commands
            public const string BuyCurrencyCommandName = "BuyCurrencyCommandName";
            public const string SendCurrencyCommandName = "SendCurrencyCommandName";
            public const string BuyVipCommandName = "BuyVipCommandName";

            // CurrencySettings
            public const string CurrencyItemID = "CurrencyItemID";
            public const string CurrencyName = "CurrencyName";
            public const string CurrencySkinID = "CurrencySkinID";
            public const string PricePerCurrencyUnit = "PricePerCurrencyUnit";
            public const string CurrencyPriceCurrency = "CurrencyPriceCurrency"; 
            public const string SatsPerCurrencyUnit = "SatsPerCurrencyUnit";
            
            // Protection Settings
            public const string MaxPurchaseAmount = "MaxPurchaseAmount";
            public const string MaxSendAmount = "MaxSendAmount";
            public const string CommandCooldownSeconds = "CommandCooldownSeconds";
            public const string MaxPendingInvoicesPerPlayer = "MaxPendingInvoicesPerPlayer";

            // Discord
            public const string DiscordChannelName = "DiscordChannelName";
            public const string DiscordWebhookUrl = "DiscordWebhookUrl";
            public const string AdminDiscordWebhookUrl = "AdminDiscordWebhookUrl";

            // InvoiceSettings
            public const string BlacklistedDomains = "BlacklistedDomains";
            public const string WhitelistedDomains = "WhitelistedDomains";
            public const string CheckIntervalSeconds = "CheckIntervalSeconds";
            public const string InvoiceTimeoutSeconds = "InvoiceTimeoutSeconds";
            public const string LNbitsApiKey = "LNbitsApiKey";
            public const string LNbitsBaseUrl = "LNbitsBaseUrl";
            public const string MaxRetries = "MaxRetries";
            public const string UseWebSockets = "UseWebSockets";
            public const string WebSocketReconnectDelay = "WebSocketReconnectDelay";

            // VIPSettings
            public const string VipPrice = "VipPrice";
            public const string VipPriceCurrency = "VipPriceCurrency"; 
            public const string VipCommand = "VipCommand";
        }

        // Configuration variables
        private int currencyItemID;
        private string buyCurrencyCommandName;
        private string sendCurrencyCommandName;
        private string buyVipCommandName;
        private double vipPrice; 
        private string vipPriceCurrency; 
        private string vipCommand;
        private string currencyName;
        private int satsPerCurrencyUnit;
        private double pricePerCurrencyUnit; 
        private string currencyPriceCurrency; 
        private string discordChannelName;
        private string adminDiscordWebhookUrl;
        private ulong currencySkinID;
        private int checkIntervalSeconds;
        private int invoiceTimeoutSeconds;
        private int maxRetries;
        private bool useWebSockets;
        private int webSocketReconnectDelay;
        private List<string> blacklistedDomains = new List<string>();
        private List<string> whitelistedDomains = new List<string>();
        
        // Protection and rate limiting variables
        private int maxPurchaseAmount;
        private int maxSendAmount;
        private int commandCooldownSeconds;
        private int maxPendingInvoicesPerPlayer;
        private Dictionary<string, DateTime> lastCommandTime = new Dictionary<string, DateTime>();

        private const string SellLogFile = "Orangemart/send_bitcoin.json";
        private const string BuyInvoiceLogFile = "Orangemart/buy_invoices.json";
        private const string OfflineQueueFile = "Orangemart/offline_rewards.json"; // NEW
        
        private LNbitsConfig config;
        private List<PendingInvoice> pendingInvoices = new List<PendingInvoice>();
        private Dictionary<string, int> retryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> offlineCurrencyQueue = new Dictionary<string, int>(); // NEW
        
        // WebSocket tracking
        private Dictionary<string, WebSocketConnection> activeWebSockets = new Dictionary<string, WebSocketConnection>();
        private readonly object webSocketLock = new object();

        // BTC Price Cache
        private double cachedBtcPriceUsd = 0;
        private DateTime lastBtcPriceFetch = DateTime.MinValue;

        // Transaction status constants
        private static class TransactionStatus
        {
            public const string INITIATED = "INITIATED";
            public const string PROCESSING = "PROCESSING";
            public const string COMPLETED = "COMPLETED";
            public const string FAILED = "FAILED";
            public const string EXPIRED = "EXPIRED";
            public const string REFUNDED = "REFUNDED";
        }

        // Price Fetching Models
        private class MempoolPriceResponse
        {
            [JsonProperty("USD")]
            public double USD { get; set; }
        }

        // WebSocket connection wrapper
        private class WebSocketConnection
        {
            public ClientWebSocket WebSocket { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; }
            public string InvoiceKey { get; set; }
            public PendingInvoice Invoice { get; set; }
            public DateTime ConnectedAt { get; set; }
            public int ReconnectAttempts { get; set; }
            public Task ListenTask { get; set; }
        }

        // WebSocket response structure
        private class WebSocketPaymentUpdate
        {
            [JsonProperty("balance")]
            public long Balance { get; set; }
            
            [JsonProperty("payment")]
            public WebSocketPayment Payment { get; set; }
        }

        private class WebSocketPayment
        {
            [JsonProperty("checking_id")]
            public string CheckingId { get; set; }
            
            [JsonProperty("pending")]
            public bool Pending { get; set; }
            
            [JsonProperty("amount")]
            public long Amount { get; set; }
            
            [JsonProperty("payment_hash")]
            public string PaymentHash { get; set; }
            
            [JsonProperty("preimage")]
            public string Preimage { get; set; }
        }

        // LNbits Configuration
        private class LNbitsConfig
        {
            public string BaseUrl { get; set; }
            public string ApiKey { get; set; }
            public string DiscordWebhookUrl { get; set; }
            public string WebSocketUrl { get; set; }

            public static LNbitsConfig ParseLNbitsConnection(string baseUrl, string apiKey, string discordWebhookUrl)
            {
                var trimmedBaseUrl = baseUrl.TrimEnd('/');
                if (!Uri.IsWellFormedUriString(trimmedBaseUrl, UriKind.Absolute))
                    throw new Exception("Invalid base URL in connection string.");

                var wsUrl = trimmedBaseUrl.Replace("https://", "wss://").Replace("http://", "ws://");

                return new LNbitsConfig
                {
                    BaseUrl = trimmedBaseUrl,
                    ApiKey = apiKey,
                    DiscordWebhookUrl = discordWebhookUrl,
                    WebSocketUrl = wsUrl
                };
            }
        }

        // Invoice and Payment Classes
        private class InvoiceResponse
        {
            [JsonProperty("bolt11")]
            public string PaymentRequest { get; set; }

            [JsonProperty("payment_hash")]
            public string PaymentHash { get; set; }
        }

        private class InvoiceResponseWrapper
        {
            [JsonProperty("data")]
            public InvoiceResponse Data { get; set; }
        }

        private class SellInvoiceLogEntry
        {
            public string TransactionId { get; set; }
            public string SteamID { get; set; }
            public string LightningAddress { get; set; }
            public string Status { get; set; }
            public bool Success { get; set; }
            public int SatsAmount { get; set; }
            public string PaymentHash { get; set; }
            public bool CurrencyReturned { get; set; }
            public DateTime Timestamp { get; set; }
            public DateTime? CompletedTimestamp { get; set; }
            public int RetryCount { get; set; }
            public string FailureReason { get; set; }
        }

        private class BuyInvoiceLogEntry
        {
            public string TransactionId { get; set; }
            public string SteamID { get; set; }
            public string InvoiceID { get; set; }
            public string Status { get; set; }
            public bool IsPaid { get; set; }
            public DateTime Timestamp { get; set; }
            public DateTime? CompletedTimestamp { get; set; }
            public int Amount { get; set; }
            public bool CurrencyGiven { get; set; }
            public bool VipGranted { get; set; }
            public int RetryCount { get; set; }
            public string PurchaseType { get; set; }
        }

        private class PendingInvoice
        {
            public string TransactionId { get; set; }
            public string RHash { get; set; }
            public IPlayer Player { get; set; }
            public int Amount { get; set; }
            public int SatsAmount { get; set; }
            public string Memo { get; set; }
            public DateTime CreatedAt { get; set; }
            public PurchaseType Type { get; set; }
            public string DiscordMessageId { get; set; } 
        }

        private enum PurchaseType
        {
            Currency,
            Vip,
            SendBitcoin
        }

        private class PaymentStatusResponse
        {
            [JsonProperty("paid")]
            public bool Paid { get; set; }

            [JsonProperty("preimage")]
            public string Preimage { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                bool configChanged = false;

                config = LNbitsConfig.ParseLNbitsConnection(
                    GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.LNbitsBaseUrl, "https://your-lnbits-instance.com", ref configChanged),
                    GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.LNbitsApiKey, "your-lnbits-admin-api-key", ref configChanged),
                    GetConfigValue(ConfigSections.Discord, ConfigKeys.DiscordWebhookUrl, "https://discord.com/api/webhooks/your_webhook_url", ref configChanged)
                );

                // Currency Settings
                currencyItemID = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.CurrencyItemID, 1776460938, ref configChanged);
                currencyName = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.CurrencyName, "blood", ref configChanged);
                satsPerCurrencyUnit = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.SatsPerCurrencyUnit, 1, ref configChanged);
                pricePerCurrencyUnit = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.PricePerCurrencyUnit, 1.0, ref configChanged);
                currencyPriceCurrency = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.CurrencyPriceCurrency, "SATS", ref configChanged);
                currencySkinID = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.CurrencySkinID, 0UL, ref configChanged);

                // Protection Settings
                maxPurchaseAmount = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.MaxPurchaseAmount, 10000, ref configChanged);
                maxSendAmount = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.MaxSendAmount, 10000, ref configChanged);
                commandCooldownSeconds = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.CommandCooldownSeconds, 0, ref configChanged);
                maxPendingInvoicesPerPlayer = GetConfigValue(ConfigSections.CurrencySettings, ConfigKeys.MaxPendingInvoicesPerPlayer, 1, ref configChanged);

                if (maxPurchaseAmount < 0) maxPurchaseAmount = 0;
                if (maxSendAmount < 0) maxSendAmount = 0;
                if (commandCooldownSeconds < 0) commandCooldownSeconds = 0;
                if (maxPendingInvoicesPerPlayer < 0) maxPendingInvoicesPerPlayer = 0;

                // Command Names
                buyCurrencyCommandName = GetConfigValue(ConfigSections.Commands, ConfigKeys.BuyCurrencyCommandName, "buyblood", ref configChanged);
                sendCurrencyCommandName = GetConfigValue(ConfigSections.Commands, ConfigKeys.SendCurrencyCommandName, "sendblood", ref configChanged);
                buyVipCommandName = GetConfigValue(ConfigSections.Commands, ConfigKeys.BuyVipCommandName, "buyvip", ref configChanged);

                // VIP Settings
                vipPrice = GetConfigValue(ConfigSections.VIPSettings, ConfigKeys.VipPrice, 1000.0, ref configChanged);
                vipPriceCurrency = GetConfigValue(ConfigSections.VIPSettings, ConfigKeys.VipPriceCurrency, "SATS", ref configChanged);
                vipCommand = GetConfigValue(ConfigSections.VIPSettings, ConfigKeys.VipCommand, "oxide.usergroup add {player} vip", ref configChanged);

                // Discord Settings
                discordChannelName = GetConfigValue(ConfigSections.Discord, ConfigKeys.DiscordChannelName, "mart", ref configChanged);
                adminDiscordWebhookUrl = GetConfigValue(ConfigSections.Discord, ConfigKeys.AdminDiscordWebhookUrl, "", ref configChanged);

                // Invoice Settings
                checkIntervalSeconds = GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.CheckIntervalSeconds, 10, ref configChanged);
                invoiceTimeoutSeconds = GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.InvoiceTimeoutSeconds, 300, ref configChanged);
                maxRetries = GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.MaxRetries, 25, ref configChanged);
                useWebSockets = GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.UseWebSockets, true, ref configChanged);
                webSocketReconnectDelay = GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.WebSocketReconnectDelay, 5, ref configChanged);

                blacklistedDomains = GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.BlacklistedDomains, new List<string> { "example.com", "blacklisted.net" }, ref configChanged)
                    .Select(d => d.ToLower()).ToList();

                whitelistedDomains = GetConfigValue(ConfigSections.InvoiceSettings, ConfigKeys.WhitelistedDomains, new List<string>(), ref configChanged)
                    .Select(d => d.ToLower()).ToList();

                if (configChanged)
                {
                    SaveConfig();
                }

                Puts($"Protection Settings: MaxPurchase={maxPurchaseAmount}, MaxSend={maxSendAmount}, Cooldown={commandCooldownSeconds}s, MaxPending={maxPendingInvoicesPerPlayer}");
                Puts($"Pricing Denominations: VIP={vipPriceCurrency}, Currency={currencyPriceCurrency}");
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load configuration: {ex.Message}");
            }
        }

        private T GetConfigValue<T>(string section, string key, T defaultValue, ref bool configChanged)
        {
            if (!(Config[section] is Dictionary<string, object> data))
            {
                data = new Dictionary<string, object>();
                Config[section] = data;
                configChanged = true;
            }

            if (!data.TryGetValue(key, out var value))
            {
                value = defaultValue;
                data[key] = value;
                configChanged = true;
            }

            try
            {
                if (value is T tValue) return tValue;
                if (typeof(T) == typeof(List<string>))
                {
                    if (value is IEnumerable<object> enumerable)
                        return (T)(object)enumerable.Select(item => item.ToString()).ToList();
                    return (T)(object)new List<string> { value.ToString() };
                }
                if (typeof(T) == typeof(ulong)) return (T)(object)Convert.ToUInt64(value);
                if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(value);
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                data[key] = defaultValue;
                configChanged = true;
                return defaultValue;
            }
        }

        protected override void LoadDefaultConfig()
        {
            Config[ConfigSections.Commands] = new Dictionary<string, object>
            {
                [ConfigKeys.BuyCurrencyCommandName] = "buyblood",
                [ConfigKeys.BuyVipCommandName] = "buyvip",
                [ConfigKeys.SendCurrencyCommandName] = "sendblood"
            };

            Config[ConfigSections.CurrencySettings] = new Dictionary<string, object>
            {
                [ConfigKeys.CurrencyItemID] = 1776460938,
                [ConfigKeys.CurrencyName] = "blood",
                [ConfigKeys.CurrencySkinID] = 0UL,
                [ConfigKeys.PricePerCurrencyUnit] = 1.0,
                [ConfigKeys.CurrencyPriceCurrency] = "SATS",
                [ConfigKeys.SatsPerCurrencyUnit] = 1,
                [ConfigKeys.MaxPurchaseAmount] = 10000,
                [ConfigKeys.MaxSendAmount] = 10000,
                [ConfigKeys.CommandCooldownSeconds] = 0,
                [ConfigKeys.MaxPendingInvoicesPerPlayer] = 1
            };

            Config[ConfigSections.Discord] = new Dictionary<string, object>
            {
                [ConfigKeys.DiscordChannelName] = "mart",
                [ConfigKeys.DiscordWebhookUrl] = "https://discord.com/api/webhooks/your_webhook_url",
                [ConfigKeys.AdminDiscordWebhookUrl] = "" 
            };

            Config[ConfigSections.InvoiceSettings] = new Dictionary<string, object>
            {
                [ConfigKeys.BlacklistedDomains] = new List<string> { "example.com", "blacklisted.net" },
                [ConfigKeys.WhitelistedDomains] = new List<string>(),
                [ConfigKeys.CheckIntervalSeconds] = 10,
                [ConfigKeys.InvoiceTimeoutSeconds] = 300,
                [ConfigKeys.LNbitsApiKey] = "your-lnbits-admin-api-key",
                [ConfigKeys.LNbitsBaseUrl] = "https://your-lnbits-instance.com",
                [ConfigKeys.MaxRetries] = 25,
                [ConfigKeys.UseWebSockets] = true,
                [ConfigKeys.WebSocketReconnectDelay] = 5
            };

            Config[ConfigSections.VIPSettings] = new Dictionary<string, object>
            {
                [ConfigKeys.VipCommand] = "oxide.usergroup add {steamid} vip",
                [ConfigKeys.VipPrice] = 1000.0,
                [ConfigKeys.VipPriceCurrency] = "SATS"
            };
        }

        private void Init()
        {
            permission.RegisterPermission("orangemart.buycurrency", this);
            permission.RegisterPermission("orangemart.sendcurrency", this);
            permission.RegisterPermission("orangemart.buyvip", this);
        }

        private void OnServerInitialized()
        {
            if (config == null)
            {
                PrintError("Plugin configuration is not properly set up.");
                return;
            }

            LoadOfflineQueue(); // Load pending items on startup

            AddCovalenceCommand(buyCurrencyCommandName, nameof(CmdBuyCurrency), "orangemart.buycurrency");
            AddCovalenceCommand(sendCurrencyCommandName, nameof(CmdSendCurrency), "orangemart.sendcurrency");
            AddCovalenceCommand(buyVipCommandName, nameof(CmdBuyVip), "orangemart.buyvip");

            RecoverInterruptedTransactions();

            timer.Every(checkIntervalSeconds, CheckPendingInvoices);
            timer.Every(300f, CleanupOldCooldowns);

            Puts($"Orangemart initialized. WebSockets: {(useWebSockets ? "Enabled" : "Disabled")}");
        }

        private void Unload()
        {
            CleanupAllWebSockets();
            SaveOfflineQueue(); // Save pending items on shutdown/reload
            pendingInvoices.Clear();
            retryCounts.Clear();
            lastCommandTime.Clear();
        }

        // --- Offline Queue Methods ---
        private void LoadOfflineQueue()
        {
            var path = Path.Combine(Interface.Oxide.DataDirectory, OfflineQueueFile);
            if (File.Exists(path))
            {
                offlineCurrencyQueue = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(path)) ?? new Dictionary<string, int>();
            }
        }

        private void SaveOfflineQueue()
        {
            var path = Path.Combine(Interface.Oxide.DataDirectory, OfflineQueueFile);
            if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(offlineCurrencyQueue, Formatting.Indented));
        }

        private void AddOfflineCurrency(string playerId, int amount)
        {
            if (offlineCurrencyQueue.ContainsKey(playerId))
                offlineCurrencyQueue[playerId] += amount;
            else
                offlineCurrencyQueue[playerId] = amount;
                
            SaveOfflineQueue();
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !offlineCurrencyQueue.ContainsKey(player.UserIDString)) return;

            int pendingAmount = offlineCurrencyQueue[player.UserIDString];
            if (pendingAmount > 0)
            {
                var currencyItem = ItemManager.CreateByItemID(currencyItemID, pendingAmount);
                if (currencyItem != null)
                {
                    if (currencySkinID > 0) currencyItem.skin = currencySkinID;
                    player.GiveItem(currencyItem);
                    
                    player.ChatMessage($"Welcome back! You received {pendingAmount} {currencyName} from pending Orangemart transactions.");
                    Puts($"Delivered {pendingAmount} offline {currencyName} to {player.displayName} ({player.UserIDString}).");
                }
            }
            
            offlineCurrencyQueue.Remove(player.UserIDString);
            SaveOfflineQueue();
        }

        // --- Pricing Methods ---
        private void GetBtcPriceUsd(Action<double> callback)
        {
            if ((DateTime.UtcNow - lastBtcPriceFetch).TotalMinutes < 5 && cachedBtcPriceUsd > 0)
            {
                callback(cachedBtcPriceUsd);
                return;
            }

            webrequest.Enqueue("https://mempool.space/api/v1/prices", null, (code, response) =>
            {
                if (code == 200 && !string.IsNullOrEmpty(response))
                {
                    try
                    {
                        var data = JsonConvert.DeserializeObject<MempoolPriceResponse>(response);
                        if (data != null && data.USD > 0)
                        {
                            cachedBtcPriceUsd = data.USD;
                            lastBtcPriceFetch = DateTime.UtcNow;
                            callback(cachedBtcPriceUsd);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        PrintError($"Failed to parse BTC price from mempool.space: {ex.Message}");
                    }
                }
                
                if (cachedBtcPriceUsd > 0) callback(cachedBtcPriceUsd);
                else callback(0);

            }, this, RequestMethod.GET);
        }

        private void CalculateSatsAmount(double fiatOrSatsPrice, string currencyType, Action<int> callback)
        {
            if (currencyType.Equals("USD", StringComparison.OrdinalIgnoreCase))
            {
                GetBtcPriceUsd(btcPrice => {
                    if (btcPrice <= 0) {
                        callback(-1); 
                        return;
                    }
                    int sats = (int)Math.Ceiling((fiatOrSatsPrice / btcPrice) * 100_000_000);
                    callback(sats);
                });
            }
            else 
            {
                callback((int)Math.Ceiling(fiatOrSatsPrice));
            }
        }

        // Protection Methods
        private bool IsOnCooldown(IPlayer player, string commandType)
        {
            if (commandCooldownSeconds <= 0) return false;
            
            string key = $"{GetPlayerId(player)}:{commandType}";
            
            if (lastCommandTime.TryGetValue(key, out DateTime lastTime))
            {
                double secondsSince = (DateTime.UtcNow - lastTime).TotalSeconds;
                if (secondsSince < commandCooldownSeconds)
                {
                    double remaining = commandCooldownSeconds - secondsSince;
                    player.Reply(Lang("CommandOnCooldown", player.Id, commandType, Math.Ceiling(remaining)));
                    return true;
                }
            }
            
            lastCommandTime[key] = DateTime.UtcNow;
            return false;
        }

        private bool HasTooManyPendingInvoices(IPlayer player)
        {
            if (maxPendingInvoicesPerPlayer == 0) return false;
            
            string playerId = GetPlayerId(player);
            int pendingCount = pendingInvoices.Count(inv => GetPlayerId(inv.Player) == playerId);
            
            if (pendingCount >= maxPendingInvoicesPerPlayer)
            {
                player.Reply(Lang("TooManyPendingInvoices", player.Id, pendingCount, maxPendingInvoicesPerPlayer));
                return true;
            }
            
            return false;
        }

        private bool ValidateSendAmount(IPlayer player, int amount, out int safeSats)
        {
            safeSats = 0;
            if (amount <= 0)
            {
                player.Reply(Lang("InvalidAmount", player.Id));
                return false;
            }
            if (maxSendAmount > 0 && amount > maxSendAmount)
            {
                player.Reply(Lang("SendAmountTooLarge", player.Id, amount, maxSendAmount, currencyName));
                return false;
            }
            
            long amountSatsLong = (long)amount * satsPerCurrencyUnit;
            if (amountSatsLong > int.MaxValue)
            {
                player.Reply(Lang("AmountCausesOverflow", player.Id));
                return false;
            }
            
            safeSats = (int)amountSatsLong;
            return true;
        }

        private void CleanupOldCooldowns()
        {
            var expiredKeys = lastCommandTime
                .Where(kvp => (DateTime.UtcNow - kvp.Value).TotalSeconds > commandCooldownSeconds * 2)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in expiredKeys)
                lastCommandTime.Remove(key);
        }

        private void CleanupAllWebSockets()
        {
            lock (webSocketLock)
            {
                foreach (var kvp in activeWebSockets)
                {
                    try
                    {
                        kvp.Value.CancellationTokenSource?.Cancel();
                        kvp.Value.WebSocket?.Dispose();
                    }
                    catch { }
                }
                activeWebSockets.Clear();
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["UsageSendCurrency"] = "Usage: /{0} <amount> <lightning_address>",
                ["NeedMoreCurrency"] = "You need more {0}. You currently have {1}.",
                ["FailedToReserveCurrency"] = "Failed to reserve currency. Please try again.",
                ["FailedToQueryLightningAddress"] = "Failed to query Lightning address for an invoice.",
                ["FailedToAuthenticate"] = "Failed to authenticate with LNbits.",
                ["InvoiceCreatedCheckDiscord"] = "Invoice created! Please check the #{0} channel on Discord.",
                ["FailedToCreateInvoice"] = "Failed to create an invoice. Please try again later.",
                ["FailedToProcessPayment"] = "Failed to process payment. Please try again later.",
                ["CurrencySentSuccess"] = "You have successfully sent {0} {1}!",
                ["PurchaseSuccess"] = "You have successfully purchased {0} {1}!",
                ["PurchaseVipSuccess"] = "You have successfully purchased VIP status!",
                ["InvalidCommandUsage"] = "Usage: /{0} <amount>",
                ["NoPermission"] = "You do not have permission to use this command.",
                ["FailedToFindBasePlayer"] = "Failed to find base player object for player {0}.",
                ["FailedToCreateCurrencyItem"] = "Failed to create {0} item for player {1}.",
                ["AddedToVipGroup"] = "Player {0} added to VIP group '{1}'.",
                ["InvoiceExpired"] = "Your invoice for ₿{0} has expired. Please try again.",
                ["BlacklistedDomain"] = "The domain '{0}' is currently blacklisted.",
                ["NotWhitelistedDomain"] = "The domain '{0}' is not whitelisted. Allowed: {1}.",
                ["InvalidLightningAddress"] = "The Lightning Address provided is invalid.",
                ["PaymentProcessing"] = "Your payment is being processed...",
                ["TransactionInitiated"] = "Transaction initiated. Processing your payment...",
                ["InvalidAmount"] = "Invalid amount. Please enter a positive number.",
                ["AmountTooLarge"] = "Amount {0} exceeds maximum limit of {1} {2}.",
                ["SendAmountTooLarge"] = "Send amount {0} exceeds maximum limit of {1} {2}.",
                ["AmountCausesOverflow"] = "Amount too large. Please use a smaller amount.",
                ["CommandOnCooldown"] = "Command '{0}' is on cooldown. Wait {1}s.",
                ["TooManyPendingInvoices"] = "You have {0} pending invoices (max: {1}).",
                ["VipPriceTooHigh"] = "VIP price is configured too high.",
                ["ProtectionLimits"] = "Orangemart Limits: Purchase max {0}, Send max {1}, Cooldown {2}s",
                ["FailedToFetchPrice"] = "Failed to fetch live exchange rate. Please try again."
            }, this);
        }

        private string Lang(string key, string userId = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, userId), args);
        }

        private string GenerateTransactionId()
        {
            return $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        // WebSocket connection management
        private async Task ConnectWebSocket(PendingInvoice invoice)
        {
            if (!useWebSockets) return;

            if (invoice.Type == PurchaseType.SendBitcoin) return;

            var wsConnection = new WebSocketConnection
            {
                WebSocket = new ClientWebSocket(),
                CancellationTokenSource = new CancellationTokenSource(),
                InvoiceKey = invoice.RHash,
                Invoice = invoice,
                ConnectedAt = DateTime.UtcNow,
                ReconnectAttempts = 0
            };

            wsConnection.WebSocket.Options.SetRequestHeader("X-Api-Key", config.ApiKey);

            lock (webSocketLock)
            {
                if (activeWebSockets.ContainsKey(invoice.RHash))
                {
                    var existing = activeWebSockets[invoice.RHash];
                    existing.CancellationTokenSource?.Cancel();
                    existing.WebSocket?.Dispose();
                }
                activeWebSockets[invoice.RHash] = wsConnection;
            }

            try
            {
                var wsUrl = $"{config.WebSocketUrl}/api/v1/ws/{invoice.RHash}";
                await wsConnection.WebSocket.ConnectAsync(new Uri(wsUrl), wsConnection.CancellationTokenSource.Token);
                
                wsConnection.ListenTask = Task.Run(async () => await ListenToWebSocket(wsConnection), wsConnection.CancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to connect WebSocket for invoice {invoice.RHash}: {ex.Message}");
                lock (webSocketLock) { activeWebSockets.Remove(invoice.RHash); }
            }
        }

        private async Task ListenToWebSocket(WebSocketConnection connection)
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);
            var messageBuilder = new StringBuilder();

            try
            {
                while (connection.WebSocket.State == WebSocketState.Open && !connection.CancellationTokenSource.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    messageBuilder.Clear();

                    do
                    {
                        result = await connection.WebSocket.ReceiveAsync(buffer, connection.CancellationTokenSource.Token);
                        if (result.MessageType == WebSocketMessageType.Text)
                            messageBuilder.Append(Encoding.UTF8.GetString(buffer.Array, 0, result.Count));
                        else if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await connection.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            break;
                        }
                    }
                    while (!result.EndOfMessage);

                    if (messageBuilder.Length > 0)
                        ProcessWebSocketMessage(connection, messageBuilder.ToString());
                }
            }
            catch { }
            finally
            {
                lock (webSocketLock)
                {
                    if (activeWebSockets.ContainsKey(connection.InvoiceKey))
                        activeWebSockets.Remove(connection.InvoiceKey);
                }
                connection.WebSocket?.Dispose();
            }
        }

        private void ProcessWebSocketMessage(WebSocketConnection connection, string message)
        {
            try
            {
                bool confirmed = false;

                try
                {
                    var simpleUpdate = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                    if (simpleUpdate != null && simpleUpdate.ContainsKey("pending") && simpleUpdate.ContainsKey("status"))
                    {
                        bool isPending = Convert.ToBoolean(simpleUpdate["pending"]);
                        string status = simpleUpdate["status"]?.ToString();
                        if (!isPending && status == "success") confirmed = true;
                    }
                }
                catch { }
                
                if (!confirmed)
                {
                    try
                    {
                        var update = JsonConvert.DeserializeObject<WebSocketPaymentUpdate>(message);
                        if (update?.Payment != null)
                        {
                            if (!update.Payment.Pending && !string.IsNullOrEmpty(update.Payment.Preimage)) confirmed = true;
                            else if (!update.Payment.Pending && update.Payment.PaymentHash?.ToLower() == connection.InvoiceKey.ToLower()) confirmed = true;
                        }
                    }
                    catch { }
                }

                if (confirmed)
                {
                    Interface.Oxide.NextTick(() => {
                        ProcessPaymentConfirmation(connection.Invoice);
                    });
                    
                    connection.CancellationTokenSource?.Cancel();
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error processing WebSocket message: {ex.Message}");
            }
        }

        private void ProcessPaymentConfirmation(PendingInvoice invoice)
        {
            if (!pendingInvoices.Contains(invoice)) return;

            pendingInvoices.Remove(invoice);
            
            string logMsg = $"Processing payment confirmation for {invoice.Player.Name} (Amount: ₿{invoice.SatsAmount}). Hash: {invoice.RHash}, Type: {invoice.Type}";
            Puts($"[ProcessPayment] {logMsg}");
            SendAdminNotification("Payment Confirmed", logMsg, 3066993); 

            switch (invoice.Type)
            {
                case PurchaseType.Currency:
                    RewardPlayer(invoice.Player, invoice.Amount);
                    UpdateBuyTransactionStatus(invoice.TransactionId, TransactionStatus.COMPLETED, true);
                    break;
                case PurchaseType.Vip:
                    GrantVip(invoice.Player);
                    UpdateBuyTransactionStatus(invoice.TransactionId, TransactionStatus.COMPLETED, true);
                    break;
                case PurchaseType.SendBitcoin:
                    invoice.Player.Reply(Lang("CurrencySentSuccess", invoice.Player.Id, invoice.Amount / satsPerCurrencyUnit, currencyName));
                    UpdateSellTransactionStatus(invoice.TransactionId, TransactionStatus.COMPLETED, true);
                    break;
            }

            retryCounts.Remove(invoice.RHash);
            
            lock (webSocketLock)
            {
                if (activeWebSockets.ContainsKey(invoice.RHash))
                {
                    activeWebSockets[invoice.RHash].CancellationTokenSource?.Cancel();
                    activeWebSockets.Remove(invoice.RHash);
                }
            }
        }

        private void RecoverInterruptedTransactions()
        {
            Puts("Checking for interrupted transactions...");

            var sellLogs = LoadSellLogData();
            foreach (var log in sellLogs.Where(l => l.Status == TransactionStatus.INITIATED || l.Status == TransactionStatus.PROCESSING))
            {
                if (!string.IsNullOrEmpty(log.PaymentHash))
                {
                    CheckInvoicePaid(log.PaymentHash, isPaid =>
                    {
                        if (isPaid) UpdateSellTransactionStatus(log.TransactionId, TransactionStatus.COMPLETED, true);
                        else UpdateSellTransactionStatus(log.TransactionId, TransactionStatus.FAILED, false, "Server interrupted");
                    });
                }
                else
                {
                    UpdateSellTransactionStatus(log.TransactionId, TransactionStatus.FAILED, false, "Interrupted before init");
                }
            }

            var buyLogs = LoadBuyLogData();
            foreach (var log in buyLogs.Where(l => l.Status == TransactionStatus.INITIATED || l.Status == TransactionStatus.PROCESSING))
            {
                if (!string.IsNullOrEmpty(log.InvoiceID))
                {
                    CheckInvoicePaid(log.InvoiceID, isPaid =>
                    {
                        if (isPaid) UpdateBuyTransactionStatus(log.TransactionId, TransactionStatus.COMPLETED, true);
                        else UpdateBuyTransactionStatus(log.TransactionId, TransactionStatus.EXPIRED, false);
                    });
                }
            }
        }

        private void CmdBuyCurrency(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission("orangemart.buycurrency")) { player.Reply(Lang("NoPermission", player.Id)); return; }
            if (IsOnCooldown(player, "buy")) return;
            if (HasTooManyPendingInvoices(player)) return;

            if (args.Length != 1 || !int.TryParse(args[0], out int amount) || amount <= 0)
            {
                player.Reply(Lang("InvalidCommandUsage", player.Id, buyCurrencyCommandName));
                return;
            }

            if (maxPurchaseAmount > 0 && amount > maxPurchaseAmount)
            {
                player.Reply(Lang("AmountTooLarge", player.Id, amount, maxPurchaseAmount, currencyName));
                return;
            }

            player.Reply(Lang("PaymentProcessing", player.Id));

            // Calculate total cost before converting
            double totalCost = amount * pricePerCurrencyUnit;

            CalculateSatsAmount(totalCost, currencyPriceCurrency, amountSats => 
            {
                if (amountSats <= 0)
                {
                    player.Reply(Lang("FailedToFetchPrice", player.Id));
                    return;
                }

                string transactionId = GenerateTransactionId();
                LogBuyInvoice(CreateBuyInvoiceLogEntry(player, null, false, amountSats, PurchaseType.Currency, 0));

                string memo = $"[Orangemart] {player.Name} buying {amount} {currencyName} " + 
                              (currencyPriceCurrency == "USD" ? $"(${totalCost:F2})" : "");

                CreateInvoice(amountSats, memo, invoiceResponse =>
                {
                    if (invoiceResponse != null)
                    {
                        UpdateBuyTransactionInvoiceId(transactionId, invoiceResponse.PaymentHash);
                        player.Reply(Lang("InvoiceCreatedCheckDiscord", player.Id, discordChannelName));

                        var pendingInvoice = new PendingInvoice
                        {
                            TransactionId = transactionId,
                            RHash = invoiceResponse.PaymentHash.ToLower(),
                            Player = player,
                            Amount = amount,
                            SatsAmount = amountSats,
                            Memo = memo,
                            CreatedAt = DateTime.UtcNow,
                            Type = PurchaseType.Currency
                        };
                        pendingInvoices.Add(pendingInvoice);
                        
                        SendInvoiceToDiscord(player, invoiceResponse.PaymentRequest, amountSats, memo, pendingInvoice);

                        Task.Run(async () => await ConnectWebSocket(pendingInvoice));
                        ScheduleInvoiceExpiry(pendingInvoice);
                    }
                    else
                    {
                        player.Reply(Lang("FailedToCreateInvoice", player.Id));
                        UpdateBuyTransactionStatus(transactionId, TransactionStatus.FAILED, false);
                    }
                });
            });
        }

        private void CmdSendCurrency(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission("orangemart.sendcurrency")) { player.Reply(Lang("NoPermission", player.Id)); return; }
            if (IsOnCooldown(player, "send")) return;
            if (HasTooManyPendingInvoices(player)) return;

            if (args.Length != 2 || !int.TryParse(args[0], out int amount))
            {
                player.Reply(Lang("UsageSendCurrency", player.Id, sendCurrencyCommandName));
                return;
            }

            if (!ValidateSendAmount(player, amount, out int satsAmount)) return;

            string lightningAddress = args[1];
            if (!IsLightningAddressAllowed(lightningAddress))
            {
                player.Reply(Lang("BlacklistedDomain", player.Id, GetDomainFromLightningAddress(lightningAddress)));
                return;
            }

            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null) return;

            if (!TryTakeCurrency(basePlayer, amount))
            {
                player.Reply(Lang("NeedMoreCurrency", player.Id, currencyName, amount));
                return;
            }

            string transactionId = GenerateTransactionId();
            LogSellTransaction(new SellInvoiceLogEntry
            {
                TransactionId = transactionId,
                SteamID = GetPlayerId(player),
                LightningAddress = lightningAddress,
                Status = TransactionStatus.INITIATED,
                Success = false,
                SatsAmount = satsAmount,
                Timestamp = DateTime.UtcNow
            });

            player.Reply(Lang("TransactionInitiated", player.Id));

            SendBitcoin(lightningAddress, satsAmount, (success, paymentHash, errorMessage) =>
            {
                if (success && !string.IsNullOrEmpty(paymentHash))
                {
                    UpdateSellTransactionPaymentHash(transactionId, paymentHash);

                    var pendingInvoice = new PendingInvoice
                    {
                        TransactionId = transactionId,
                        RHash = paymentHash.ToLower(),
                        Player = player,
                        Amount = satsAmount,
                        SatsAmount = satsAmount,
                        Memo = $"Sending {amount} {currencyName} to {lightningAddress}",
                        CreatedAt = DateTime.UtcNow,
                        Type = PurchaseType.SendBitcoin
                    };
                    
                    pendingInvoices.Add(pendingInvoice);
                    
                    string logMsg = $"Outbound payment to {lightningAddress} initiated by {player.Name} for ₿{satsAmount}. PaymentHash: {paymentHash}";
                    Puts(logMsg);
                    SendAdminNotification("Outbound Payment Initiated", logMsg, 16753920);

                    CheckInvoicePaid(paymentHash, isPaid => 
                    {
                        if (isPaid)
                        {
                            ProcessPaymentConfirmation(pendingInvoice);
                        }
                    });
                }
                else
                {
                    string replyMsg = errorMessage ?? Lang("FailedToProcessPayment", player.Id);
                    player.Reply(replyMsg);
                    
                    Puts($"[Orangemart] Send failed for {player.Name} ({lightningAddress}). Reason: {replyMsg}");

                    UpdateSellTransactionStatus(transactionId, TransactionStatus.FAILED, false, replyMsg, true);
                    
                    // Offline safe refund
                    ReturnCurrency(GetPlayerId(player), amount);
                }
            });
        }

        private void CmdBuyVip(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission("orangemart.buyvip")) { player.Reply(Lang("NoPermission", player.Id)); return; }
            if (IsOnCooldown(player, "vip")) return;
            if (HasTooManyPendingInvoices(player)) return;

            player.Reply(Lang("PaymentProcessing", player.Id));

            CalculateSatsAmount(vipPrice, vipPriceCurrency, amountSats => 
            {
                if (amountSats <= 0)
                {
                    player.Reply(Lang("FailedToFetchPrice", player.Id));
                    return;
                }
                
                if (amountSats > int.MaxValue)
                {
                    player.Reply(Lang("VipPriceTooHigh", player.Id));
                    return;
                }

                string transactionId = GenerateTransactionId();
                LogBuyInvoice(CreateBuyInvoiceLogEntry(player, null, false, amountSats, PurchaseType.Vip, 0));

                string memo = $"[Orangemart] {player.Name} buying VIP Status " + 
                              (vipPriceCurrency == "USD" ? $"(${vipPrice:F2})" : "");

                CreateInvoice(amountSats, memo, invoiceResponse =>
                {
                    if (invoiceResponse != null)
                    {
                        UpdateBuyTransactionInvoiceId(transactionId, invoiceResponse.PaymentHash);
                        player.Reply(Lang("InvoiceCreatedCheckDiscord", player.Id, discordChannelName));

                        var pendingInvoice = new PendingInvoice
                        {
                            TransactionId = transactionId,
                            RHash = invoiceResponse.PaymentHash.ToLower(),
                            Player = player,
                            Amount = amountSats,
                            SatsAmount = amountSats,
                            Memo = memo,
                            CreatedAt = DateTime.UtcNow,
                            Type = PurchaseType.Vip
                        };
                        pendingInvoices.Add(pendingInvoice);

                        SendInvoiceToDiscord(player, invoiceResponse.PaymentRequest, amountSats, memo, pendingInvoice);
                        
                        Task.Run(async () => await ConnectWebSocket(pendingInvoice));
                        ScheduleInvoiceExpiry(pendingInvoice);
                    }
                    else
                    {
                        player.Reply(Lang("FailedToCreateInvoice", player.Id));
                        UpdateBuyTransactionStatus(transactionId, TransactionStatus.FAILED, false);
                    }
                });
            });
        }

        [ChatCommand("orangelimits")]
        private void CmdPlayerLimits(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage(Lang("ProtectionLimits", player.UserIDString, maxPurchaseAmount, maxSendAmount, commandCooldownSeconds));
        }

        private bool TryTakeCurrency(BasePlayer player, int amount)
        {
            var collected = new List<Item>();
            int taken = player.inventory.Take(collected, currencyItemID, amount);
            
            if (taken == amount)
            {
                foreach (var item in collected) item.Remove();
                return true;
            }

            foreach (var item in collected)
            {
                player.GiveItem(item);
                if (item.parent == null) item.Drop(player.transform.position + new UnityEngine.Vector3(0f, 1.5f, 0f), UnityEngine.Vector3.zero);
            }
            return false;
        }

        private void CheckPendingInvoices()
        {
            var currentInvoices = pendingInvoices.ToList();

            foreach (var invoice in currentInvoices)
            {
                string localPaymentHash = invoice.RHash;
                
                CheckInvoicePaid(localPaymentHash, isPaid =>
                {
                    if (isPaid)
                    {
                        ProcessPaymentConfirmation(invoice);
                    }
                    else
                    {
                        if (!retryCounts.ContainsKey(localPaymentHash)) retryCounts[localPaymentHash] = 0;
                        retryCounts[localPaymentHash]++;

                        if (retryCounts[localPaymentHash] >= maxRetries)
                        {
                            ExpireInvoice(invoice, "Max Retries Reached");
                        }
                    }
                });
            }
        }

        private void CheckInvoicePaid(string paymentHash, Action<bool> callback)
        {
            string url = $"{config.BaseUrl}/api/v1/payments/{paymentHash.ToLower()}";
            var headers = new Dictionary<string, string> { { "Content-Type", "application/json" }, { "X-Api-Key", config.ApiKey } };

            MakeWebRequest(url, null, (code, response) =>
            {
                if (code != 200 || string.IsNullOrEmpty(response)) { callback(false); return; }
                try
                {
                    var paymentStatus = JsonConvert.DeserializeObject<PaymentStatusResponse>(response);
                    callback(paymentStatus != null && paymentStatus.Paid);
                }
                catch { callback(false); }
            }, RequestMethod.GET, headers);
        }

        private bool IsLightningAddressAllowed(string lightningAddress)
        {
            string domain = GetDomainFromLightningAddress(lightningAddress);
            if (string.IsNullOrEmpty(domain)) return false;
            return whitelistedDomains.Any() ? whitelistedDomains.Contains(domain) : !blacklistedDomains.Contains(domain);
        }

        private string GetDomainFromLightningAddress(string lightningAddress)
        {
            if (string.IsNullOrEmpty(lightningAddress)) return null;
            var parts = lightningAddress.Split('@');
            return parts.Length == 2 ? parts[1].ToLower() : null;
        }

        private void SendBitcoin(string lightningAddress, int satsAmount, Action<bool, string, string> callback)
        {
            ResolveLightningAddress(lightningAddress, satsAmount, (bolt11, errorMessage) =>
            {
                if (string.IsNullOrEmpty(bolt11)) { 
                    callback(false, null, errorMessage ?? "Failed to resolve Lightning Address."); 
                    return; 
                }

                SendPayment(bolt11, satsAmount, (success, paymentHash) =>
                {
                    if (!success) {
                        callback(false, null, "Failed to route payment via LNbits.");
                    } else {
                        callback(true, paymentHash, null);
                    }
                });
            });
        }

        private void ScheduleInvoiceExpiry(PendingInvoice pendingInvoice)
        {
            timer.Once(invoiceTimeoutSeconds, () =>
            {
                if (pendingInvoices.Contains(pendingInvoice))
                {
                    ExpireInvoice(pendingInvoice, "Timeout Timer");
                }
            });
        }

        private void SendPayment(string bolt11, int satsAmount, Action<bool, string> callback)
        {
            string url = $"{config.BaseUrl}/api/v1/payments";
            var jsonBody = JsonConvert.SerializeObject(new { @out = true, bolt11 = bolt11 });
            var headers = new Dictionary<string, string> { { "X-Api-Key", config.ApiKey }, { "Content-Type", "application/json" } };

            MakeWebRequest(url, jsonBody, (code, response) =>
            {
                if (code != 200 && code != 201) { callback(false, null); return; }
                try
                {
                    InvoiceResponse invoiceResponse = null;
                    try { invoiceResponse = JsonConvert.DeserializeObject<InvoiceResponseWrapper>(response)?.Data; } catch { }
                    if (invoiceResponse == null) invoiceResponse = JsonConvert.DeserializeObject<InvoiceResponse>(response);

                    if (!string.IsNullOrEmpty(invoiceResponse?.PaymentHash)) callback(true, invoiceResponse.PaymentHash);
                    else callback(false, null);
                }
                catch { callback(false, null); }
            }, RequestMethod.POST, headers);
        }

        private void CreateInvoice(int amountSats, string memo, Action<InvoiceResponse> callback)
        {
            string url = $"{config.BaseUrl}/api/v1/payments";
            var jsonBody = JsonConvert.SerializeObject(new { @out = false, amount = amountSats, memo = memo });
            var headers = new Dictionary<string, string> { { "X-Api-Key", config.ApiKey }, { "Content-Type", "application/json" } };

            MakeWebRequest(url, jsonBody, (code, response) =>
            {
                if (code != 200 && code != 201) { callback(null); return; }
                try
                {
                    var invoiceResponse = JsonConvert.DeserializeObject<InvoiceResponse>(response);
                    callback(invoiceResponse);
                }
                catch { callback(null); }
            }, RequestMethod.POST, headers);
        }

        private string GetPlayerId(IPlayer player)
        {
            return (player.Object as BasePlayer)?.UserIDString ?? player.Id;
        }

        private void MakeWebRequest(string url, string jsonData, Action<int, string> callback, RequestMethod method = RequestMethod.GET, Dictionary<string, string> headers = null)
        {
            webrequest.Enqueue(url, jsonData, (code, response) => callback(code, response), this, method, headers);
        }

        private void ResolveLightningAddress(string lightningAddress, int amountSats, Action<string, string> callback)
        {
            var parts = lightningAddress.Split('@');
            if (parts.Length != 2) { callback(null, "Invalid Lightning Address format."); return; }

            string domain = parts[1];
            string username = parts[0];
            string lnurlEndpoint = $"https://{domain}/.well-known/lnurlp/{username}";

            MakeWebRequest(lnurlEndpoint, null, (code, response) =>
            {
                if (code == 404) {
                    callback(null, $"User '{username}' not found at {domain}.");
                    return;
                }
                if (code >= 500 && code <= 599) {
                    callback(null, $"The receiving server ({domain}) is currently down or returned an error (HTTP {code}).");
                    return;
                }
                if (code == 0) { 
                    callback(null, $"Could not connect to {domain}. The domain might be offline.");
                    return;
                }
                if (code != 200 || string.IsNullOrEmpty(response)) { 
                    callback(null, $"Unexpected response from {domain} (HTTP {code})."); 
                    return; 
                }

                try
                {
                    var lnurlResponse = JsonConvert.DeserializeObject<LNURLResponse>(response);
                    if (lnurlResponse == null || string.IsNullOrEmpty(lnurlResponse.Callback)) { 
                        callback(null, $"The server {domain} returned an invalid response."); 
                        return; 
                    }

                    long amountMsat = (long)amountSats * 1000;
                    string callbackUrl = $"{lnurlResponse.Callback}?amount={amountMsat}";

                    MakeWebRequest(callbackUrl, null, (payCode, payResponse) =>
                    {
                        if (payCode != 200 || string.IsNullOrEmpty(payResponse)) { 
                            callback(null, $"Failed to fetch invoice from {domain} (HTTP {payCode})."); 
                            return; 
                        }
                        try
                        {
                            var payAction = JsonConvert.DeserializeObject<LNURLPayResponse>(payResponse);
                            if (string.IsNullOrEmpty(payAction?.Pr)) {
                                callback(null, $"The server {domain} did not provide a valid invoice.");
                                return;
                            }
                            callback(payAction.Pr, null);
                        }
                        catch { callback(null, $"Error parsing invoice data from {domain}."); }
                    });
                }
                catch { callback(null, $"Error parsing data from {domain}."); }
            });
        }

        private class LNURLResponse
        {
            [JsonProperty("callback")] public string Callback { get; set; }
        }

        private class LNURLPayResponse
        {
            [JsonProperty("pr")] public string Pr { get; set; }
        }

        private void RewardPlayer(IPlayer player, int amount)
        {
            var basePlayer = player.Object as BasePlayer;
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                AddOfflineCurrency(GetPlayerId(player), amount);
                Puts($"Player {player.Name} is offline. Saved {amount} {currencyName} to offline queue.");
                return;
            }

            var currencyItem = ItemManager.CreateByItemID(currencyItemID, amount);
            if (currencyItem != null)
            {
                if (currencySkinID > 0) currencyItem.skin = currencySkinID;
                
                basePlayer.GiveItem(currencyItem);

                if (currencyItem.parent == null)
                {
                    currencyItem.Drop(basePlayer.transform.position + new UnityEngine.Vector3(0f, 1.5f, 0f), UnityEngine.Vector3.zero);
                    player.Reply($"Inventory full! {amount} {currencyName} dropped on ground.");
                }
                else 
                {
                    player.Reply($"You have successfully purchased {amount} {currencyName}!");
                }
            }
        }

        private void GrantVip(IPlayer player)
        {
            player.Reply("You have successfully purchased VIP status!");
            string id = GetPlayerId(player);
            string cmd = vipCommand.Replace("{player}", player.Name).Replace("{steamid}", id).Replace("{userid}", id);
            server.Command(cmd);
        }

        private void ReturnCurrency(string playerId, int amount)
        {
            var basePlayer = BasePlayer.FindByID(Convert.ToUInt64(playerId));
            if (basePlayer == null || !basePlayer.IsConnected)
            {
                AddOfflineCurrency(playerId, amount);
                Puts($"Player {playerId} is offline. Saved {amount} {currencyName} to offline refund queue.");
                return;
            }

            var returnedCurrency = ItemManager.CreateByItemID(currencyItemID, amount);
            if (returnedCurrency != null)
            {
                if (currencySkinID > 0) returnedCurrency.skin = currencySkinID;
                
                basePlayer.GiveItem(returnedCurrency);
                
                if (returnedCurrency.parent == null)
                {
                    returnedCurrency.Drop(basePlayer.transform.position + new UnityEngine.Vector3(0f, 1.5f, 0f), UnityEngine.Vector3.zero);
                }
                Puts($"Returned {amount} {currencyName} to player {basePlayer.UserIDString}.");
            }
        }

        private void LogSellTransaction(SellInvoiceLogEntry logEntry)
        {
            var logs = LoadSellLogData();
            var idx = logs.FindIndex(l => l.TransactionId == logEntry.TransactionId);
            if (idx >= 0) logs[idx] = logEntry; else logs.Add(logEntry);
            SaveSellLogData(logs);
        }

        private void UpdateSellTransactionStatus(string transactionId, string status, bool success, string failureReason = null, bool currencyReturned = false)
        {
            var logs = LoadSellLogData();
            var entry = logs.FirstOrDefault(l => l.TransactionId == transactionId);
            if (entry != null)
            {
                entry.Status = status;
                entry.Success = success;
                entry.CompletedTimestamp = DateTime.UtcNow;
                entry.CurrencyReturned = currencyReturned;
                if (!string.IsNullOrEmpty(failureReason)) entry.FailureReason = failureReason;
                SaveSellLogData(logs);
            }
        }

        private void UpdateSellTransactionPaymentHash(string transactionId, string paymentHash)
        {
            var logs = LoadSellLogData();
            var entry = logs.FirstOrDefault(l => l.TransactionId == transactionId);
            if (entry != null) { entry.PaymentHash = paymentHash; SaveSellLogData(logs); }
        }

        private List<SellInvoiceLogEntry> LoadSellLogData()
        {
            var path = Path.Combine(Interface.Oxide.DataDirectory, SellLogFile);
            return File.Exists(path) ? JsonConvert.DeserializeObject<List<SellInvoiceLogEntry>>(File.ReadAllText(path)) : new List<SellInvoiceLogEntry>();
        }

        private void SaveSellLogData(List<SellInvoiceLogEntry> data)
        {
            var path = Path.Combine(Interface.Oxide.DataDirectory, SellLogFile);
            if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        private void LogBuyInvoice(BuyInvoiceLogEntry logEntry)
        {
            var logs = LoadBuyLogData();
            var idx = logs.FindIndex(l => l.TransactionId == logEntry.TransactionId);
            if (idx >= 0) logs[idx] = logEntry; else logs.Add(logEntry);
            SaveBuyLogData(logs);
        }

        private void UpdateBuyTransactionStatus(string transactionId, string status, bool isPaid)
        {
            var logs = LoadBuyLogData();
            var entry = logs.FirstOrDefault(l => l.TransactionId == transactionId);
            if (entry != null)
            {
                entry.Status = status;
                entry.IsPaid = isPaid;
                entry.CompletedTimestamp = DateTime.UtcNow;
                if (isPaid)
                {
                    if (entry.PurchaseType == "Currency") entry.CurrencyGiven = true;
                    else if (entry.PurchaseType == "VIP") entry.VipGranted = true;
                }
                SaveBuyLogData(logs);
            }
        }

        private void UpdateBuyTransactionInvoiceId(string transactionId, string invoiceId)
        {
            var logs = LoadBuyLogData();
            var entry = logs.FirstOrDefault(l => l.TransactionId == transactionId);
            if (entry != null) { entry.InvoiceID = invoiceId; SaveBuyLogData(logs); }
        }

        private List<BuyInvoiceLogEntry> LoadBuyLogData()
        {
            var path = Path.Combine(Interface.Oxide.DataDirectory, BuyInvoiceLogFile);
            return File.Exists(path) ? JsonConvert.DeserializeObject<List<BuyInvoiceLogEntry>>(File.ReadAllText(path)) ?? new List<BuyInvoiceLogEntry>() : new List<BuyInvoiceLogEntry>();
        }

        private void SaveBuyLogData(List<BuyInvoiceLogEntry> data)
        {
            var path = Path.Combine(Interface.Oxide.DataDirectory, BuyInvoiceLogFile);
            if (!Directory.Exists(Path.GetDirectoryName(path))) Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        private BuyInvoiceLogEntry CreateBuyInvoiceLogEntry(IPlayer player, string invoiceID, bool isPaid, int amount, PurchaseType type, int retryCount)
        {
            return new BuyInvoiceLogEntry
            {
                TransactionId = GenerateTransactionId(),
                SteamID = GetPlayerId(player),
                InvoiceID = invoiceID,
                Status = isPaid ? TransactionStatus.COMPLETED : TransactionStatus.FAILED,
                IsPaid = isPaid,
                Timestamp = DateTime.UtcNow,
                CompletedTimestamp = DateTime.UtcNow,
                Amount = amount,
                CurrencyGiven = isPaid && type == PurchaseType.Currency,
                VipGranted = isPaid && type == PurchaseType.Vip,
                RetryCount = retryCount,
                PurchaseType = type == PurchaseType.Currency ? "Currency" : "VIP"
            };
        }

        private void SendAdminNotification(string title, string message, int color)
        {
            if (string.IsNullOrEmpty(adminDiscordWebhookUrl)) return;

            var payload = new
            {
                embeds = new[]
                {
                    new
                    {
                        title = title,
                        description = message,
                        color = color,
                        timestamp = DateTime.UtcNow.ToString("o")
                    }
                }
            };

            var headers = new Dictionary<string, string> { { "Content-Type", "application/json" } };
            MakeWebRequest(adminDiscordWebhookUrl, JsonConvert.SerializeObject(payload), (code, response) => { }, RequestMethod.POST, headers);
        }

        private void SendInvoiceToDiscord(IPlayer player, string invoice, int amountSats, string memo, PendingInvoice pendingInvoice)
        {
            if (string.IsNullOrEmpty(config.DiscordWebhookUrl)) return;

            string qrCodeUrl = $"https://api.qrserver.com/v1/create-qr-code/?data={Uri.EscapeDataString(invoice)}&size=200x200";
            var payload = new
            {
                content = $"**{player.Name}**, please pay **₿{amountSats}**.",
                embeds = new[]
                {
                    new
                    {
                        title = "Payment Invoice",
                        description = $"{memo}\n\n```\n{invoice}\n```",
                        image = new { url = qrCodeUrl },
                        fields = new[] { new { name = "Amount", value = $"₿{amountSats}", inline = true } }
                    }
                }
            };

            var headers = new Dictionary<string, string> { { "Content-Type", "application/json" } };
            
            string url = $"{config.DiscordWebhookUrl}?wait=true";

            MakeWebRequest(url, JsonConvert.SerializeObject(payload), (code, response) => 
            {
                if (code >= 200 && code < 300 && !string.IsNullOrEmpty(response))
                {
                    try 
                    {
                        var discordResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                        if (discordResponse.ContainsKey("id"))
                        {
                            pendingInvoice.DiscordMessageId = discordResponse["id"].ToString();
                        }
                    }
                    catch {}
                }
                else
                {
                    PrintError($"Failed to send Discord invoice. HTTP Code: {code}");
                }
            }, RequestMethod.POST, headers);
        }

        private void EditDiscordMessage(string messageId, IPlayer player, int amountSats)
        {
            if (string.IsNullOrEmpty(config.DiscordWebhookUrl) || string.IsNullOrEmpty(messageId)) return;
            
            string editUrl = $"{config.DiscordWebhookUrl}/messages/{messageId}";
            
            var payload = new
            {
                content = $"~~**{player.Name}**, please pay **₿{amountSats}**.~~",
                embeds = new[]
                {
                    new
                    {
                        title = "Invoice Expired",
                        description = "This invoice has expired due to timeout. Please request a new one.",
                        color = 15158332, 
                        fields = new[] { new { name = "Status", value = "EXPIRED", inline = true } }
                    }
                }
            };

            MakeWebRequest(editUrl, JsonConvert.SerializeObject(payload), (code, response) => 
            {
                if (code >= 200 && code < 300) Puts($"Marked Discord message {messageId} as expired.");
            }, RequestMethod.PATCH, new Dictionary<string, string> { { "Content-Type", "application/json" } });
        }

        private void CancelLNbitsPayment(string paymentHash)
        {
            string url = $"{config.BaseUrl}/api/v1/payments/{paymentHash}";
            var headers = new Dictionary<string, string> { { "X-Api-Key", config.ApiKey } };

            MakeWebRequest(url, null, (code, response) =>
            {
                Puts($"Attempted to cancel/delete payment {paymentHash} in LNbits. Code: {code}");
            }, RequestMethod.DELETE, headers);
        }

        private void ExpireInvoice(PendingInvoice pendingInvoice, string reason)
        {
            if (pendingInvoices.Contains(pendingInvoice))
            {
                pendingInvoices.Remove(pendingInvoice);
            }

            lock (webSocketLock)
            {
                if (activeWebSockets.ContainsKey(pendingInvoice.RHash))
                {
                    activeWebSockets[pendingInvoice.RHash].CancellationTokenSource?.Cancel();
                    activeWebSockets.Remove(pendingInvoice.RHash);
                }
            }
            
            if (retryCounts.ContainsKey(pendingInvoice.RHash))
            {
                retryCounts.Remove(pendingInvoice.RHash);
            }

            if (!string.IsNullOrEmpty(pendingInvoice.DiscordMessageId))
            {
                EditDiscordMessage(pendingInvoice.DiscordMessageId, pendingInvoice.Player, pendingInvoice.SatsAmount);
            }

            CancelLNbitsPayment(pendingInvoice.RHash);

            if (pendingInvoice.Type == PurchaseType.SendBitcoin)
            {
                var basePlayer = pendingInvoice.Player.Object as BasePlayer;
                if (basePlayer != null) 
                {
                    pendingInvoice.Player.Reply("Payment is delayed. If it takes more than a few hours please open a ticket in Discord.");
                    Puts($"[ALERT] Outbound payment stuck for {pendingInvoice.Player.Name}. Hash: {pendingInvoice.RHash}. Do NOT refund unless confirmed failed in LNbits.");
                }
                UpdateSellTransactionStatus(pendingInvoice.TransactionId, TransactionStatus.EXPIRED, false, reason, true);
            }
            else
            {
                UpdateBuyTransactionStatus(pendingInvoice.TransactionId, TransactionStatus.EXPIRED, false);
                pendingInvoice.Player.Reply("Your purchase invoice has expired.");
            }
            
            Puts($"Invoice {pendingInvoice.RHash} expired. Reason: {reason}");
        }
    }
}