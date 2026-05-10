using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Database;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Group Web Sync", "Orangemart", "1.1.0")]
    [Description("Syncs Oxide groups and player rewards to a central MySQL database")]
    public class GroupWebSync : CovalencePlugin
    {
        private Configuration _config;
        private readonly Oxide.Core.MySql.Libraries.MySql _mySql = Interface.Oxide.GetLibrary<Oxide.Core.MySql.Libraries.MySql>();
        private Oxide.Core.Database.Connection _dbConnection;

        #region Configuration
        private class Configuration
        {
            [JsonProperty("Server Identifier (e.g., orange or mandarin)")]
            public string ServerName = "orange";

            [JsonProperty("MySQL Host IP")]
            public string Host = "127.0.0.1";

            [JsonProperty("MySQL Port")]
            public int Port = 3306;

            [JsonProperty("MySQL Database Name")]
            public string Database = "rust_server";

            [JsonProperty("MySQL Username")]
            public string Username = "username";

            [JsonProperty("MySQL Password")]
            public string Password = "password";
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new JsonException();
            } catch {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion

        #region Initialization & Database
        private void OnServerInitialized()
        {
            if (_mySql == null)
            {
                PrintError("MySQL library not found! Is the Oxide.Ext.MySql extension installed?");
                return;
            }

            try
            {
                _dbConnection = _mySql.OpenDb(_config.Host, _config.Port, _config.Database, _config.Username, _config.Password, this);
                
                // Existing User Groups Table
                Sql groupSql = Sql.Builder.Append(@"CREATE TABLE IF NOT EXISTS user_groups (
                                                steam_id VARCHAR(50) NOT NULL,
                                                group_name VARCHAR(50) NOT NULL,
                                                PRIMARY KEY (steam_id, group_name)
                                              );");
                
                // New Player Rewards Table
                Sql rewardSql = Sql.Builder.Append(@"CREATE TABLE IF NOT EXISTS player_rewards (
                                                steam_id VARCHAR(50) NOT NULL,
                                                server_name VARCHAR(50) NOT NULL,
                                                total_amount BIGINT NOT NULL,
                                                PRIMARY KEY (steam_id, server_name)
                                              );");
                
                _mySql.ExecuteNonQuery(groupSql, _dbConnection, delegate(int rows) { });
                _mySql.ExecuteNonQuery(rewardSql, _dbConnection, delegate(int rows) 
                {
                    Puts("Successfully connected to MySQL and verified both table structures.");
                });
            }
            catch (Exception ex)
            {
                PrintError($"Failed to connect to MySQL: {ex.Message}");
            }
        }

        private void Unload()
        {
            if (_dbConnection != null)
            {
                _mySql.CloseDb(_dbConnection);
            }
        }
        #endregion

        #region Group Sync Hooks & Commands
        [HookMethod("OnUserGroupAdded")]
        private void OnUserGroupAdded(string id, string groupName)
        {
            if (_dbConnection == null) return;
            Sql sql = Sql.Builder.Append("REPLACE INTO user_groups (steam_id, group_name) VALUES (@0, @1);", id, groupName);
            _mySql.Insert(sql, _dbConnection, delegate(int rows) { });
        }

        [HookMethod("OnUserGroupRemoved")]
        private void OnUserGroupRemoved(string id, string groupName)
        {
            if (_dbConnection == null) return;
            Sql sql = Sql.Builder.Append("DELETE FROM user_groups WHERE steam_id = @0 AND group_name = @1;", id, groupName);
            _mySql.Delete(sql, _dbConnection, delegate(int rows) { });
        }

        [Command("groupsync.force")]
        private void ForceGroupSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            int count = 0;
            string[] allGroups = permission.GetGroups();

            foreach (string group in allGroups)
            {
                string[] usersInGroup = permission.GetUsersInGroup(group);
                foreach (string userId in usersInGroup)
                {
                    Sql sql = Sql.Builder.Append("REPLACE INTO user_groups (steam_id, group_name) VALUES (@0, @1);", userId, group);
                    _mySql.Insert(sql, _dbConnection, delegate(int rows) { });
                    count++;
                }
            }

            player.Reply($"Queued {count} group assignments for asynchronous sync to MySQL.");
        }
        #endregion

        #region Reward Sync Logic
        // Data classes to match ClaimedRewards.json structure
        private class RewardData
        {
            [JsonProperty("claims")]
            public List<ClaimEntry> Claims = new List<ClaimEntry>();
        }

        private class ClaimEntry
        {
            [JsonProperty("steamid")]
            public string SteamId { get; set; }
            
            [JsonProperty("amount_claimed")]
            public long AmountClaimed { get; set; }
        }

        [Command("rewardsync.force")]
        private void ForceRewardSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;
            if (_dbConnection == null) return;

            // Read the JSON file from /oxide/data/ClaimPlayerRewards/ClaimedRewards.json
            var data = Interface.Oxide.DataFileSystem.ReadObject<RewardData>("ClaimPlayerRewards/ClaimedRewards");

            if (data == null || data.Claims == null || data.Claims.Count == 0)
            {
                player.Reply("No rewards found in ClaimedRewards.json to sync.");
                return;
            }

            // Aggregate multiple claims per SteamID into a single total
            Dictionary<string, long> aggregatedRewards = new Dictionary<string, long>();
            foreach (var claim in data.Claims)
            {
                if (string.IsNullOrEmpty(claim.SteamId)) continue;

                if (aggregatedRewards.ContainsKey(claim.SteamId))
                {
                    aggregatedRewards[claim.SteamId] += claim.AmountClaimed;
                }
                else
                {
                    aggregatedRewards[claim.SteamId] = claim.AmountClaimed;
                }
            }

            int count = 0;
            foreach (var kvp in aggregatedRewards)
            {
                // Push the aggregated total to MySQL along with the specific server name
                Sql sql = Sql.Builder.Append("REPLACE INTO player_rewards (steam_id, server_name, total_amount) VALUES (@0, @1, @2);", kvp.Key, _config.ServerName, kvp.Value);
                _mySql.Insert(sql, _dbConnection, delegate(int rows) { });
                count++;
            }

            player.Reply($"Aggregated and queued {count} player reward totals for asynchronous sync to MySQL.");
        }
        #endregion
    }
}