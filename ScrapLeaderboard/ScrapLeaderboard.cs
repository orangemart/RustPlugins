/*
================================================================================================
  ScrapLeaderboard
  Version: 2.1.0
  Author: Orangemart
================================================================================================

  OVERVIEW:
  This plugin handles scrap deposits, enforces real-time limits, logs transactions, 
  and updates the ServerInfo leaderboard automatically.

  PERMISSIONS:
  - scrapleaderboard.place :  Allows player to use /depositbox to place a box.
  - scrapleaderboard.admin :  Allows access to /scrapleaderboard command.

  COMMANDS:
  - /depositbox              :  Give yourself a deployable deposit box.
  - /scrapleaderboard [pool] :  (Admin) Generates summary files, updates ServerInfo, and reloads it.
                                [pool] is optional prize pool amount (default 100,000).

  DATA FILES (oxide/data/ScrapLeaderboard/):
  - ScrapLog.json     : Permanent transaction log.
  - ScrapSummary.json : Calculated totals and percentages.
  - ScrapClaims.json  : Reward payouts based on prize pool.

  CONFIGURATION (oxide/config/ScrapLeaderboard.json):
  - DepositBoxSkinID  : 3616815672
  - DepositItemID     : -932201673 (Scrap)
  - MaxDepositLimit   : 250000

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
    [Info("ScrapLeaderboard", "Orangemart", "2.1.0")]
    [Description("Handles scrap deposits, enforces limits, and updates the ServerInfo leaderboard.")]
    public class ScrapLeaderboard : CovalencePlugin
    {
        // ==========================================================================
        // Configuration
        // ==========================================================================
        private int DepositItemID;
        private ulong DepositBoxSkinID;
        private int MaxDepositLimit;
        
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
            
            // Handle loading and potential migration from old filenames
            LoadAndMigrateData();
            
            RecalculateTotals();

            permission.RegisterPermission(PermPlace, this);
            permission.RegisterPermission(PermAdmin, this);
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
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is StorageContainer container && container.TryGetComponent(out DepositBoxRestriction restriction))
                    UnityEngine.Object.Destroy(restriction);
            }
            instance = null;
        }

        void OnEntitySpawned(StorageContainer container)
        {
            if (container == null || container.skinID != DepositBoxSkinID) return;
            
            if (!container.TryGetComponent(out DepositBoxRestriction mono))
            {
                mono = container.gameObject.AddComponent<DepositBoxRestriction>();
                mono.container = container.inventory;
                mono.InitDepositBox();
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file.");
            Config["DepositItemID"] = -932201673; // Scrap Item ID
            Config["DepositBoxSkinID"] = 3616815672;
            Config["MaxDepositLimit"] = 250000;
            SaveConfig();
        }

        private void LoadConfiguration()
        {
            DepositItemID = Convert.ToInt32(Config["DepositItemID"], CultureInfo.InvariantCulture);
            DepositBoxSkinID = Convert.ToUInt64(Config["DepositBoxSkinID"], CultureInfo.InvariantCulture);
            MaxDepositLimit = Convert.ToInt32(Config["MaxDepositLimit"], CultureInfo.InvariantCulture);
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

            // 1. Determine Prize Pool (Default: 100,000)
            int prizePool = 100000;
            if (args.Length > 0 && int.TryParse(args[0], out int parsedAmount))
            {
                prizePool = parsedAmount;
            }

            // 2. Generate Summary Files
            GenerateDepositSummaryFiles(prizePool);
            player.Reply($"✅ Summary files generated in 'oxide/data/{DataFolder}/' (Pool: {prizePool:N0})");

            // 3. Update ServerInfo.json
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

                // Prepare localized text lines
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
                    // Remove existing Leaderboard tabs
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

                    // 4. Reload ServerInfo
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
        // Core Logic & Data Management
        // ==========================================================================

        private void LoadAndMigrateData()
        {
            // 1. Try to load the new file format: oxide/data/ScrapLeaderboard/ScrapLog.json
            string newFilePath = $"{DataFolder}/{LogFileName}";
            
            // 2. If new file doesn't exist, check for the old one to migrate
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(newFilePath))
            {
                if (Interface.Oxide.DataFileSystem.ExistsDatafile("DepositBoxLog"))
                {
                    Puts("⚠️ Old data file found. Migrating 'DepositBoxLog' to 'ScrapLeaderboard/ScrapLog'...");
                    depositLog = Interface.Oxide.DataFileSystem.ReadObject<DepositLog>("DepositBoxLog");
                    
                    // Save immediately to new location
                    SaveDepositLog(); 
                }
                else
                {
                    // No old data, start fresh
                    depositLog = new DepositLog();
                }
            }
            else
            {
                // Load existing new file
                depositLog = Interface.Oxide.DataFileSystem.ReadObject<DepositLog>(newFilePath);
            }
        }

        private void SaveDepositLog()
        {
            // Saves to: oxide/data/ScrapLeaderboard/ScrapLog.json
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

        private void LogDeposit(BasePlayer player, int amount)
        {
            depositLog.Deposits.Add(new DepositEntry
            {
                SteamId = player.UserIDString,
                Timestamp = DateTime.UtcNow.ToString("o"),
                AmountDeposited = amount
            });

            if (!playerTotalsCache.ContainsKey(player.UserIDString))
                playerTotalsCache[player.UserIDString] = 0;

            playerTotalsCache[player.UserIDString] += amount;
            int newTotal = playerTotalsCache[player.UserIDString];

            SaveDepositLog();

            player.ChatMessage(Lang("DepositRecorded", player.UserIDString)
                .Replace("{amount}", amount.ToString("N0"))
                .Replace("{total}", newTotal.ToString("N0")));
        }

        // ==========================================================================
        // Helper Classes
        // ==========================================================================

        public class DepositBoxRestriction : FacepunchBehaviour
        {
            public ItemContainer container;
            public void InitDepositBox()
            {
                container.canAcceptItem += CanAcceptItem;
                container.onItemAddedRemoved += OnItemAddedRemoved;
            }

            private bool CanAcceptItem(Item item, int targetPos)
            {
                if (item == null || item.info == null || item.info.itemid != ScrapLeaderboard.instance.DepositItemID)
                    return false;

                var player = item.GetOwnerPlayer();
                if (player == null) return false;

                int currentTotal = 0;
                if (ScrapLeaderboard.instance.playerTotalsCache.TryGetValue(player.UserIDString, out int total))
                {
                    currentTotal = total;
                }

                if (currentTotal + item.amount > ScrapLeaderboard.instance.MaxDepositLimit)
                {
                    int remainingAllowance = ScrapLeaderboard.instance.MaxDepositLimit - currentTotal;
                    
                    if (remainingAllowance <= 0)
                        player.ChatMessage($"You have reached the deposit limit of {ScrapLeaderboard.instance.MaxDepositLimit:N0}. Current total: {currentTotal:N0}.");
                    else
                        player.ChatMessage($"This deposit would exceed your limit. You can only deposit {remainingAllowance:N0} more. (Current: {currentTotal:N0}, Max: {ScrapLeaderboard.instance.MaxDepositLimit:N0})");

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
                if (!added || item.info.itemid != ScrapLeaderboard.instance.DepositItemID) return;

                if (ScrapLeaderboard.instance.depositTrack.TryGetValue(item.uid, out string playerId))
                {
                    ScrapLeaderboard.instance.NextTick(() =>
                    {
                        if (item == null || item.amount < 1) return;

                        var player = BasePlayer.Find(playerId);
                        if (player != null)
                        {
                            ScrapLeaderboard.instance.LogDeposit(player, item.amount);
                        }

                        ScrapLeaderboard.instance.depositTrack.Remove(item.uid);
                        item.Remove();
                    });
                }
            }

            public void Destroy()
            {
                container.canAcceptItem -= CanAcceptItem;
                container.onItemAddedRemoved -= OnItemAddedRemoved;
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
        // Data & Localization
        // ==========================================================================

        private string Lang(string key, string id = null) => lang.GetMessage(key, this, id);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission to place this box.",
                ["BoxGiven"] = "You have received a Deposit Box.",
                ["DepositRecorded"] = "Your deposit of {amount} scrap has been recorded successfully. You have deposited a total of {total}.",
                ["ButtonText"] = "Leaderboard",
                ["HeaderText"] = "Top Scrap Depositors",
                ["TitleLine1"] = "🏆 Top Scrap Depositors This Wipe",
                ["TitleLine2"] = "Total Deposited: {0} scrap"
            }, this, "en");
        }
    }
}