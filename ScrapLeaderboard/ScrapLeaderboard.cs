/*
================================================================================================
  ScrapLeaderboard
  Version: 2.3.0
  Author: Orangemart
================================================================================================

  OVERVIEW:
  This plugin handles scrap deposits, enforces real-time limits, logs transactions, 
  and updates the ServerInfo leaderboard automatically.

  UPDATES v2.3.0:
  - ADDED: Configuration option 'EnableTeamSplitting' to toggle the entire splitting feature.
  - v2.2.3: Improved chat messages for split deposits.
  - v2.2.2: Fixed NullReference/RPC crashes on reload.

  CONFIGURATION (oxide/config/ScrapLeaderboard.json):
  - EnableTeamSplitting: (bool) Should deposits be split among teammates? (Default: true)
  - SplitWithOfflineTeammates: (bool) Distribute scrap to offline team members? (Default: false)
================================================================================================
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ScrapLeaderboard", "Orangemart", "2.4.0")]
    [Description("Handles scrap deposits, enforces limits, and updates the ServerInfo leaderboard.")]
    public class ScrapLeaderboard : CovalencePlugin
    {
        // ==========================================================================
        // Configuration
        // ==========================================================================
        private int DepositItemID;
        private ulong DepositBoxSkinID;
        private int MaxDepositLimit;
        private bool EnableTeamSplitting;
        private bool SplitWithOfflineTeammates;
        
        // ==========================================================================
        // Constants & Paths
        // ==========================================================================
        private const string ServerInfoPath = "oxide/config/ServerInfo.json";
        private const string PermPlace = "scrapleaderboard.place";
        private const string PermAdmin = "scrapleaderboard.admin";
        
        // Data Paths
        private const string DataFolder = "ScrapLeaderboard";
        private const string LogFileName = "ScrapLog"; 

        // ==========================================================================
        // Data Structures
        // ==========================================================================
        private DepositLog depositLog;
        private Dictionary<ItemId, string> depositTrack = new Dictionary<ItemId, string>();
        public Dictionary<string, int> playerTotalsCache = new Dictionary<string, int>();

        private static ScrapLeaderboard instance;

        // ==========================================================================
        // Oxide Hooks
        // ==========================================================================
        void Init()
        {
            instance = this;
            LoadConfiguration();
            LoadAndMigrateData();
            RecalculateTotals();

            permission.RegisterPermission(PermPlace, this);
            permission.RegisterPermission(PermAdmin, this);
            
            // Force refresh components on reload
            NextTick(() => {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    if (entity is StorageContainer container)
                        OnEntitySpawned(container);
                }
            });
        }

        void OnServerInitialized(bool initial)
        {
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is StorageContainer container)
                    OnEntitySpawned(container);
            }
        }

        void Unload()
        {
            var boxes = UnityEngine.Object.FindObjectsOfType<DepositBoxRestriction>();
            foreach (var box in boxes)
            {
                UnityEngine.Object.Destroy(box);
            }
            instance = null;
        }

        void OnEntitySpawned(StorageContainer container)
        {
            if (container == null || container.skinID != DepositBoxSkinID) return;
            
            var existing = container.GetComponent<DepositBoxRestriction>();
            if (existing != null) UnityEngine.Object.Destroy(existing);

            var mono = container.gameObject.AddComponent<DepositBoxRestriction>();
            mono.container = container.inventory;
            mono.InitDepositBox();
        }

        // ==========================================================================
        // Configuration Loading
        // ==========================================================================
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file.");
            Config["DepositItemID"] = -932201673; 
            Config["DepositBoxSkinID"] = 3616815672;
            Config["MaxDepositLimit"] = 250000;
            Config["EnableTeamSplitting"] = true;
            Config["SplitWithOfflineTeammates"] = false;
            SaveConfig();
        }

        private void LoadConfiguration()
        {
            bool configUpdated = false;

            T GetConfig<T>(string key, T defaultValue)
            {
                if (Config[key] == null)
                {
                    Config[key] = defaultValue;
                    configUpdated = true;
                }
                return (T)Convert.ChangeType(Config[key], typeof(T));
            }

            DepositItemID = GetConfig("DepositItemID", -932201673);
            DepositBoxSkinID = GetConfig("DepositBoxSkinID", 3616815672ul);
            MaxDepositLimit = GetConfig("MaxDepositLimit", 250000);
            EnableTeamSplitting = GetConfig("EnableTeamSplitting", true);
            SplitWithOfflineTeammates = GetConfig("SplitWithOfflineTeammates", false);

            if (configUpdated)
            {
                SaveConfig();
                Puts("Configuration file updated with new options.");
            }
        }

        // ==========================================================================
        // Commands
        // ==========================================================================

        [Command("depositbox")]
        private void CmdGiveDepositBox(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermPlace))
            {
                player.Reply(Lang("NoPermission", player.Id));
                return;
            }

            var rustPlayer = player.Object as BasePlayer;
            if (rustPlayer == null) return;

            rustPlayer.inventory.containerMain.GiveItem(ItemManager.CreateByItemID(833533164, 1, DepositBoxSkinID));
            player.Reply(Lang("BoxGiven", player.Id));
        }

        [Command("scrapleaderboard")]
        private void CmdScrapLeaderboard(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermAdmin) && !player.IsAdmin)
            {
                player.Reply("You must be an admin to use this command.");
                return;
            }

            int prizePool = 100000;
            if (args.Length > 0 && int.TryParse(args[0], out int parsedAmount))
            {
                prizePool = parsedAmount;
            }

            GenerateDepositSummaryFiles(prizePool);
            player.Reply($"✅ Summary files generated in 'oxide/data/{DataFolder}/' (Pool: {prizePool:N0})");

            if (!File.Exists(ServerInfoPath))
            {
                player.Reply("❌ ServerInfo.json not found. Skipping leaderboard update.");
                return;
            }

            try
            {
                long totalDeposited = playerTotalsCache.Values.Sum();
                var sortedPlayers = playerTotalsCache
                    .OrderByDescending(kv => kv.Value)
                    .Take(40)
                    .ToList();

                var allLines = new List<string>
                {
                    Lang("TitleLine1", player.Id),
                    string.Format(Lang("TitleLine2", player.Id), totalDeposited.ToString("N0")),
                    ""
                };

                for (int i = 0; i < sortedPlayers.Count; i++)
                {
                    var kv = sortedPlayers[i];
                    var steamId = kv.Key;
                    var name = covalence.Players.FindPlayerById(steamId)?.Name ?? steamId;
                    var deposited = kv.Value;
                    double percentage = totalDeposited > 0 ? (double)deposited / totalDeposited * 100 : 0;
                    allLines.Add($"{i + 1}. {name} - {deposited:N0} ({percentage:F2}%)");
                }

                var page1Lines = allLines.Take(23).ToList();
                var page2Lines = allLines.Skip(23).ToList();

                var leaderboardTab = new JObject
                {
                    ["ButtonText"] = Lang("ButtonText", player.Id),
                    ["HeaderText"] = Lang("HeaderText", player.Id),
                    ["Pages"] = new JArray
                    {
                        new JObject { ["TextLines"] = JArray.FromObject(page1Lines), ["ImageSettings"] = new JArray() },
                        new JObject { ["TextLines"] = JArray.FromObject(page2Lines), ["ImageSettings"] = new JArray() }
                    },
                    ["TabButtonAnchor"] = 4,
                    ["TabButtonFontSize"] = 16,
                    ["HeaderAnchor"] = 0,
                    ["HeaderFontSize"] = 32,
                    ["TextFontSize"] = 16,
                    ["TextAnchor"] = 3,
                    ["OxideGroup"] = ""
                };

                var serverInfoRaw = File.ReadAllText(ServerInfoPath);
                var serverInfo = JsonConvert.DeserializeObject<Dictionary<string, object>>(serverInfoRaw);
                
                if (serverInfo["settings"] is JObject settings && settings["Tabs"] is JArray tabs)
                {
                    for (int i = tabs.Count - 1; i >= 0; i--)
                    {
                        if (tabs[i]["ButtonText"]?.ToString() == Lang("ButtonText", player.Id))
                        {
                            tabs.RemoveAt(i);
                        }
                    }
                    tabs.Add(leaderboardTab);
                    
                    File.WriteAllText(ServerInfoPath, JsonConvert.SerializeObject(serverInfo, Formatting.Indented));
                    Puts("✅ ServerInfo.json updated with 2-page leaderboard.");

                    server.Command("oxide.reload ServerInfo");
                    player.Reply("✅ Leaderboard updated and ServerInfo reloaded!");
                }
                else
                {
                    player.Reply("❌ 'settings' or 'Tabs' section missing in ServerInfo.json.");
                }
            }
            catch (Exception ex)
            {
                player.Reply($"❌ Error updating leaderboard: {ex.Message}");
                Puts($"❌ Error: {ex.Message}");
            }
        }

        // ==========================================================================
        // Core Logic
        // ==========================================================================

        private void LoadAndMigrateData()
        {
            string newFilePath = $"{DataFolder}/{LogFileName}";
            
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(newFilePath))
            {
                if (Interface.Oxide.DataFileSystem.ExistsDatafile("DepositBoxLog"))
                {
                    Puts("⚠️ Old data file found. Migrating 'DepositBoxLog' to 'ScrapLeaderboard/ScrapLog'...");
                    depositLog = Interface.Oxide.DataFileSystem.ReadObject<DepositLog>("DepositBoxLog");
                    SaveDepositLog(); 
                }
                else
                {
                    depositLog = new DepositLog();
                }
            }
            else
            {
                depositLog = Interface.Oxide.DataFileSystem.ReadObject<DepositLog>(newFilePath);
            }
        }

        private void SaveDepositLog()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{DataFolder}/{LogFileName}", depositLog);
        }

        private void RecalculateTotals()
        {
            playerTotalsCache.Clear();
            if (depositLog?.Deposits == null) return;

            foreach (var entry in depositLog.Deposits)
            {
                if (!playerTotalsCache.ContainsKey(entry.SteamId))
                    playerTotalsCache[entry.SteamId] = 0;

                playerTotalsCache[entry.SteamId] += entry.AmountDeposited;
            }
        }

        private void GenerateDepositSummaryFiles(int prizePool)
        {
            int totalDeposits = playerTotalsCache.Values.Sum();
            var summary = new Dictionary<string, object>();
            var claims = new Dictionary<string, int>();
            var csvBuilder = new StringBuilder();
            csvBuilder.AppendLine("steamid,total_deposited,percentage,sats_reward");

            foreach (var entry in playerTotalsCache)
            {
                double percentage = totalDeposits > 0 ? (double)entry.Value / totalDeposits : 0;
                int reward = (int)Math.Round(percentage * prizePool);

                summary[entry.Key] = new
                {
                    total_deposited = entry.Value,
                    percentage = percentage * 100,
                    sats_reward = reward
                };

                claims[entry.Key] = reward;
                csvBuilder.AppendLine($"{entry.Key},{entry.Value},{(percentage * 100).ToString("F2", CultureInfo.InvariantCulture)},{reward}");
            }

            string dirPath = Interface.Oxide.DataDirectory + $"/{DataFolder}";
            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

            File.WriteAllText(Path.Combine(dirPath, "ScrapSummary.json"), JsonConvert.SerializeObject(summary, Formatting.Indented));
            File.WriteAllText(Path.Combine(dirPath, "ScrapClaims.json"), JsonConvert.SerializeObject(claims, Formatting.Indented));
            File.WriteAllText(Path.Combine(dirPath, "ScrapSummary.csv"), csvBuilder.ToString());
        }

        private void LogDeposit(string userId, int amount, string sourceName = null, int originalTotal = 0, int teammateCount = 0)
        {
            depositLog.Deposits.Add(new DepositEntry
            {
                SteamId = userId,
                Timestamp = DateTime.UtcNow.ToString("o"),
                AmountDeposited = amount
            });

            if (!playerTotalsCache.ContainsKey(userId))
                playerTotalsCache[userId] = 0;

            playerTotalsCache[userId] += amount;
            int newTotal = playerTotalsCache[userId];

            SaveDepositLog();

            var player = BasePlayer.Find(userId);
            if (player != null && player.IsConnected)
            {
                // Case 1: Received from someone else
                if (sourceName != null)
                {
                    player.ChatMessage(Lang("DepositSplitReceived", userId)
                        .Replace("{amount}", amount.ToString("N0"))
                        .Replace("{source}", sourceName)
                        .Replace("{total}", newTotal.ToString("N0")));
                }
                // Case 2: Deposited by self, but it was split (Requires >0 teammates)
                else if (originalTotal > 0 && teammateCount > 0)
                {
                    player.ChatMessage(Lang("DepositSplitSelf", userId)
                        .Replace("{original}", originalTotal.ToString("N0"))
                        .Replace("{count}", teammateCount.ToString())
                        .Replace("{amount}", amount.ToString("N0")) // The amount THEY kept
                        .Replace("{total}", newTotal.ToString("N0")));
                }
                // Case 3: Deposited by self, no split (standard)
                else
                {
                    player.ChatMessage(Lang("DepositRecorded", userId)
                        .Replace("{amount}", amount.ToString("N0"))
                        .Replace("{total}", newTotal.ToString("N0")));
                }
            }
        }

        // ==========================================================================
        // Helper Classes
        // ==========================================================================

        public class DepositBoxRestriction : FacepunchBehaviour
{
    public ItemContainer container;
    
    public void InitDepositBox()
    {
        if (container == null) return;
        container.canAcceptItem += CanAcceptItem;
        container.onItemAddedRemoved += OnItemAddedRemoved;
    }

    // --- NEW HELPER METHODS ---
    private int GetPlayerTotal(string userId)
    {
        if (ScrapLeaderboard.instance.playerTotalsCache.TryGetValue(userId, out int total))
            return total;
        return 0;
    }

    private int GetPlayerCapacity(string userId)
    {
        return Math.Max(0, ScrapLeaderboard.instance.MaxDepositLimit - GetPlayerTotal(userId));
    }

    private int GetTeamCapacity(BasePlayer depositor)
    {
        int capacity = GetPlayerCapacity(depositor.UserIDString);
        if (!ScrapLeaderboard.instance.EnableTeamSplitting || depositor.Team == null) 
            return capacity;

        foreach (var memberId in depositor.Team.members)
        {
            if (memberId == depositor.userID) continue;
            bool isEligible = ScrapLeaderboard.instance.SplitWithOfflineTeammates;
            
            if (!isEligible)
            {
                var teammate = BasePlayer.FindByID(memberId);
                if (teammate != null && teammate.IsConnected) isEligible = true;
            }

            if (isEligible)
            {
                capacity += GetPlayerCapacity(memberId.ToString());
            }
        }
        return capacity;
    }
    // --------------------------

    private bool CanAcceptItem(Item item, int targetPos)
    {
        if (ScrapLeaderboard.instance == null) return false;

        if (item == null || item.info == null || item.info.itemid != ScrapLeaderboard.instance.DepositItemID)
            return false;

        var player = item.GetOwnerPlayer();
        if (player == null) return false;

        // Check if the team as a whole has ANY capacity left.
        // If they do, we let it in and handle partial refunds in OnItemAddedRemoved.
        int teamCapacity = GetTeamCapacity(player);
        if (teamCapacity <= 0)
{
    player.ChatMessage(ScrapLeaderboard.instance.Lang("TeamLimitReached", player.UserIDString));
    return false;
}

        if (ScrapLeaderboard.instance.depositTrack.ContainsKey(item.uid))
            ScrapLeaderboard.instance.depositTrack[item.uid] = player.UserIDString;
        else
            ScrapLeaderboard.instance.depositTrack.Add(item.uid, player.UserIDString);

        return true;
    }

    private void OnItemAddedRemoved(Item item, bool added)
    {
        if (ScrapLeaderboard.instance == null) return;
        if (!added || item == null || item.info == null || item.info.itemid != ScrapLeaderboard.instance.DepositItemID) return;

        if (ScrapLeaderboard.instance.depositTrack.TryGetValue(item.uid, out string playerId))
        {
            ScrapLeaderboard.instance.timer.Once(0.1f, () =>
            {
                if (ScrapLeaderboard.instance == null) return;
                if (item == null || item.amount < 1) return;

                var depositor = BasePlayer.Find(playerId);
                if (depositor == null)
                {
                    // Fallback if depositor disconnects during the 0.1s delay
                    int cap = GetPlayerCapacity(playerId);
                    int logAmt = Math.Min(item.amount, cap);
                    if (logAmt > 0) ScrapLeaderboard.instance.LogDeposit(playerId, logAmt);
                    
                    ScrapLeaderboard.instance.depositTrack.Remove(item.uid);
                    item.Remove();
                    return;
                }

                List<string> beneficiaries = new List<string> { depositor.UserIDString };

                if (ScrapLeaderboard.instance.EnableTeamSplitting && depositor.Team != null)
                {
                    foreach (var memberId in depositor.Team.members)
                    {
                        if (memberId == depositor.userID) continue;
                        bool isEligible = ScrapLeaderboard.instance.SplitWithOfflineTeammates;
                        
                        if (!isEligible)
                        {
                            var teammate = BasePlayer.FindByID(memberId);
                            if (teammate != null && teammate.IsConnected) isEligible = true;
                        }

                        if (isEligible) beneficiaries.Add(memberId.ToString());
                    }
                }

                int totalAmount = item.amount;
                int count = beneficiaries.Count;
                int baseSplit = totalAmount / count;
                int remainder = totalAmount % count;

                int overflowPool = 0;
                Dictionary<string, int> allocations = new Dictionary<string, int>();

                // PASS 1: Allocate up to limits and gather excess into the overflow pool
                foreach (var userId in beneficiaries)
                {
                    int intendedAmount = baseSplit;
                    if (userId == depositor.UserIDString) intendedAmount += remainder;

                    int allowance = GetPlayerCapacity(userId);

                    if (intendedAmount > allowance)
                    {
                        allocations[userId] = allowance;
                        overflowPool += (intendedAmount - allowance); // Send excess back to depositor's pool
                    }
                    else
                    {
                        allocations[userId] = intendedAmount;
                    }
                }

                // PASS 2: Re-allocate overflow pool back to the depositor
                int physicalRefund = 0;
                if (overflowPool > 0)
                {
                    int depositorCurrentAllocation = allocations[depositor.UserIDString];
                    int depositorTotal = GetPlayerTotal(depositor.UserIDString);
                    int depositorRemainingCapacity = ScrapLeaderboard.instance.MaxDepositLimit - (depositorTotal + depositorCurrentAllocation);

                    if (overflowPool > depositorRemainingCapacity)
                    {
                        // Depositor also hit their limit. Refund the rest as physical scrap.
                        allocations[depositor.UserIDString] += Math.Max(0, depositorRemainingCapacity);
                        physicalRefund = overflowPool - Math.Max(0, depositorRemainingCapacity);
                    }
                    else
                    {
                        // Depositor absorbs the teammates' excess
                        allocations[depositor.UserIDString] += overflowPool;
                    }
                }

                // Log all valid allocations
                foreach (var kvp in allocations)
                {
                    string userId = kvp.Key;
                    int amountToLog = kvp.Value;

                    if (amountToLog > 0)
                    {
                        if (userId == depositor.UserIDString)
                        {
                            ScrapLeaderboard.instance.LogDeposit(userId, amountToLog, null, totalAmount - physicalRefund, count - 1);
                        }
                        else
                        {
                            ScrapLeaderboard.instance.LogDeposit(userId, amountToLog, depositor.displayName);
                        }
                    }
                }

                // Return un-depositable scrap back to the player's inventory
                if (physicalRefund > 0)
{
    Item refundItem = ItemManager.CreateByItemID(ScrapLeaderboard.instance.DepositItemID, physicalRefund);
    if (refundItem != null)
    {
        depositor.GiveItem(refundItem);
        string refundMsg = ScrapLeaderboard.instance.Lang("DepositRefunded", depositor.UserIDString)
                                            .Replace("{refund}", physicalRefund.ToString("N0"));
        depositor.ChatMessage(refundMsg);
    }
}

                ScrapLeaderboard.instance.depositTrack.Remove(item.uid);
                item.Remove();
            });
        }
    }

    public void Destroy()
    {
        if (container != null)
        {
            container.canAcceptItem -= CanAcceptItem;
            container.onItemAddedRemoved -= OnItemAddedRemoved;
        }
        UnityEngine.Object.Destroy(this);
    }
}

        private class DepositLog
        {
            [JsonProperty("deposits")]
            public List<DepositEntry> Deposits { get; set; } = new List<DepositEntry>();
        }

        private class DepositEntry
        {
            [JsonProperty("steamid")]
            public string SteamId { get; set; }
            [JsonProperty("timestamp")]
            public string Timestamp { get; set; }
            [JsonProperty("amount_deposited")]
            public int AmountDeposited { get; set; }
        }

        // ==========================================================================
        // Localization
        // ==========================================================================
        public string Lang(string key, string id = null) => lang.GetMessage(key, this, id);

        protected override void LoadDefaultMessages()
{
    lang.RegisterMessages(new Dictionary<string, string>
    {
        ["NoPermission"] = "You do not have permission to place this box.",
        ["BoxGiven"] = "You have received a Deposit Box.",
        ["DepositRecorded"] = "Deposit: {amount} scrap. Total: {total}.",
        ["DepositSplitReceived"] = "Received split deposit: {amount} scrap from {source}. Total: {total}.",
        
        // Updated to reflect that the depositor might have absorbed overflow
        ["DepositSplitSelf"] = "You deposited {original} scrap. Split with {count} teammate(s). You were credited {amount}. Your Total: {total}.",
        
        // New messages for the dynamic limits & refunds
        ["TeamLimitReached"] = "Limit reached. You and your eligible teammates have hit the maximum deposit limit.",
        ["DepositRefunded"] = "Partial limit reached. Returned {refund} scrap to your inventory.",
        
        ["ButtonText"] = "Leaderboard",
        ["HeaderText"] = "Top Scrap Depositors",
        ["TitleLine1"] = "🏆 Top Scrap Depositors This Wipe",
        ["TitleLine2"] = "Total Deposited: {0} scrap"
    }, this, "en");
}
    }
}