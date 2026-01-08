using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("StudyBuddy", "Orangemart", "1.0.0")]
    [Description("A lightweight blueprint sharing plugin that allows online teammates to copy your homework and unlock blueprints")]
    class StudyBuddy : RustPlugin
    {
        #region Fields & Config

        private const string usePermission = "studybuddy.use";
        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Share Tech Tree Blueprints")] public bool TechTreeSharingEnabled = true;
            [JsonProperty("Items Blocked from Sharing")] public HashSet<string> BlockedItems = new HashSet<string>();
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(usePermission, this);
        }

        // Hook: Research Table
        private void OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player == null || item == null || action != "study") return;
            TryShareBlueprint(item.blueprintTargetDef, player);
        }

        // Hook: Tech Tree
        private void OnTechTreeNodeUnlocked(Workbench workbench, TechTreeData.NodeInstance node, BasePlayer player)
        {
            if (!config.TechTreeSharingEnabled || node == null || player == null) return;
            TryShareBlueprint(node.itemDef, player);
        }

        #endregion

        #region Core Logic

        private void TryShareBlueprint(ItemDefinition itemDef, BasePlayer sharer)
        {
            if (itemDef == null || sharer == null) return;
            if (!permission.UserHasPermission(sharer.UserIDString, usePermission)) return;
            if (config.BlockedItems.Contains(itemDef.shortname)) return;

            // 1. Get Team Members
            var team = RelationshipManager.ServerInstance.FindPlayersTeam(sharer.userID);
            if (team == null || team.members.Count <= 1) return;

            // 2. Identify Blueprint IDs (Main item + any sub-items)
            var blueprintsToShare = new List<int> { itemDef.itemid };
            if (itemDef.Blueprint?.additionalUnlocks != null)
            {
                foreach (var subItem in itemDef.Blueprint.additionalUnlocks)
                    blueprintsToShare.Add(subItem.itemid);
            }

            int sharedCount = 0;

            // 3. Loop through team members
            foreach (ulong targetId in team.members)
            {
                if (targetId == sharer.userID) continue;

                // STRICT PERFORMANCE CHECK:
                // We only look for players currently in memory (online).
                // We do not touch the disk for offline players.
                BasePlayer onlinePlayer = RelationshipManager.FindByID(targetId);

                if (onlinePlayer != null && onlinePlayer.IsConnected)
                {
                    if (UnlockForOnlinePlayer(onlinePlayer, blueprintsToShare))
                    {
                        sharedCount++;
                        Message(onlinePlayer, $"<color=#ffff00>{sharer.displayName}</color> shared {itemDef.displayName.translated} with you.");
                    }
                }
            }

            if (sharedCount > 0)
            {
               Message(sharer, $"Shared <color=#ffff00>{itemDef.displayName.translated}</color> with {sharedCount} online team mate(s).");
            }
        }

        private bool UnlockForOnlinePlayer(BasePlayer player, List<int> blueprintIds)
        {
            // Because the player is online, their data is already in RAM.
            // This operation is purely memory manipulation. Zero Disk I/O.
            var playerInfo = player.PersistantPlayerInfo;
            if (playerInfo == null) return false;

            bool learnedSomething = false;

            foreach (int id in blueprintIds)
            {
                if (!playerInfo.unlockedItems.Contains(id))
                {
                    playerInfo.unlockedItems.Add(id);
                    
                    // Send RPC so the client UI updates immediately (green checkmark appears)
                    player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), id);
                    
                    // Update stats just for fun
                    player.stats.Add("blueprint_studied", 1);
                    
                    learnedSomething = true;
                }
            }

            if (learnedSomething)
            {
                // Sync the changes to the network so the server acknowledges them
                player.SendNetworkUpdateImmediate();
                return true;
            }

            return false;
        }

        #endregion

        #region Helpers

        private void Message(BasePlayer player, string msg)
        {
            if (player == null) return;
            player.ChatMessage($"<color=#D85540>[StudyBuddy]</color> {msg}");
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { config = Config.ReadObject<Configuration>(); }
            catch { LoadDefaultConfig(); }
        }
        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion
    }
}