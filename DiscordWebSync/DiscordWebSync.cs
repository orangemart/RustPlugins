using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Database;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("Discord Web Sync", "Orangemart", "1.0.5")]
    [Description("Syncs DiscordAuth links to a central MySQL database asyncronously")]
    public class DiscordWebSync : CovalencePlugin
    {
        private Configuration _config;
        private readonly Oxide.Core.MySql.Libraries.MySql _mySql = Interface.Oxide.GetLibrary<Oxide.Core.MySql.Libraries.MySql>();
        private Oxide.Core.Database.Connection _dbConnection;

        #region Configuration
        private class Configuration
        {
            [JsonProperty("MySQL Host IP")]
            public string Host = "127.0.0.1";

            [JsonProperty("MySQL Port")]
            public int Port = 3306;

            [JsonProperty("MySQL Database Name")]
            public string Database = "discord_auth";

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
                
                Sql sql = Sql.Builder.Append(@"CREATE TABLE IF NOT EXISTS discord_web_links (
                                                steam_id VARCHAR(50) PRIMARY KEY,
                                                discord_id VARCHAR(50) NOT NULL
                                              );");
                
                _mySql.ExecuteNonQuery(sql, _dbConnection, delegate(int rows) 
                {
                    Puts("Successfully connected to MySQL and verified table structure in the background.");
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

        #region Hooks (Real-Time Sync)
        [HookMethod("OnDiscordPlayerLinked")]
        private void OnDiscordPlayerLinked(IPlayer player, string discordId)
        {
            // Note: Updated this hook to accept a string to avoid complex Discord extension object requirements
            if (_dbConnection == null) return;
            Sql sql = Sql.Builder.Append("REPLACE INTO discord_web_links (steam_id, discord_id) VALUES (@0, @1);", player.Id, discordId);
            _mySql.Insert(sql, _dbConnection, delegate(int rows) { });
        }

        [HookMethod("OnDiscordPlayerUnlinked")]
        private void OnDiscordPlayerUnlinked(IPlayer player, string discordId)
        {
            if (_dbConnection == null) return;
            Sql sql = Sql.Builder.Append("DELETE FROM discord_web_links WHERE steam_id = @0;", player.Id);
            _mySql.Delete(sql, _dbConnection, delegate(int rows) { });
        }
        #endregion

        #region Data Mapping for Force Sync
        // This perfectly matches the structure of oxide/data/DiscordAuth.json
        private class DiscordAuthData
        {
            public Dictionary<string, string> Players = new Dictionary<string, string>();
        }
        #endregion

        #region Manual Sync Command
        [Command("websync.force")]
        private void ForceSyncCommand(IPlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) return;

            // Read the JSON file directly from the hard drive, completely bypassing cross-plugin Call issues
            var data = Interface.Oxide.DataFileSystem.ReadObject<DiscordAuthData>("DiscordAuth");

            if (data == null || data.Players == null || data.Players.Count == 0)
            {
                player.Reply("No links found in the DiscordAuth JSON file to sync.");
                return;
            }

            int count = 0;
            foreach (var link in data.Players)
            {
                Sql sql = Sql.Builder.Append("REPLACE INTO discord_web_links (steam_id, discord_id) VALUES (@0, @1);", link.Key, link.Value);
                _mySql.Insert(sql, _dbConnection, delegate(int rows) { });
                count++;
            }

            player.Reply($"Successfully force-synced {count} players to MySQL asynchronously!");
        }
        #endregion
    }
}