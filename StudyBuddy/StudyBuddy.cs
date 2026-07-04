using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("StudyBuddy", "Orangemart", "1.0.3")]
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

        private void OnServerInitialized()
        {
            Puts($"StudyBuddy has been initialized and is ready! TechTreeSharingEnabled: {config?.TechTreeSharingEnabled}");
        }

        // Hook: Research Table
        private void OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player == null || item == null || action != "study") return;
            TryShareBlueprint(item.blueprintTargetDef, player);
        }

        // Hook: Tech Tree (Pre-Unlock)
        private void OnTechTreeNodeUnlock(Workbench workbench, object nodeOrItemDef, BasePlayer player)
        {
            HandleTechTreeUnlock(nodeOrItemDef, player);
        }

        // Hook: Tech Tree (Post-Unlock)
        private void OnTechTreeNodeUnlocked(Workbench workbench, object nodeOrItemDef, BasePlayer player)
        {
            HandleTechTreeUnlock(nodeOrItemDef, player);
        }

        private void HandleTechTreeUnlock(object nodeOrItemDef, BasePlayer player)
        {
            if (nodeOrItemDef == null || player == null || !config.TechTreeSharingEnabled) return;

            ItemDefinition itemDef = nodeOrItemDef as ItemDefinition;
            if (itemDef == null)
            {
                // Reflection to extract ItemDefinition from the node
                var fields = nodeOrItemDef.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                foreach (var field in fields)
                {
                    if (field.FieldType == typeof(ItemDefinition))
                    {
                        itemDef = field.GetValue(nodeOrItemDef) as ItemDefinition;
                        if (itemDef != null)
                        {
                            break;
                        }
                    }
                }

                if (itemDef == null)
                {
                    var props = nodeOrItemDef.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    foreach (var prop in props)
                    {
                        if (prop.PropertyType == typeof(ItemDefinition))
                        {
                            itemDef = prop.GetValue(nodeOrItemDef) as ItemDefinition;
                            if (itemDef != null)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            if (itemDef == null)
            {
                return;
            }

            TryShareBlueprint(itemDef, player);
        }

        #endregion

        #region Core Logic

        private void TryShareBlueprint(ItemDefinition itemDef, BasePlayer sharer)
        {
            if (itemDef == null || sharer == null) return;

            if (!permission.UserHasPermission(sharer.UserIDString, usePermission))
            {
                return;
            }

            if (config.BlockedItems.Contains(itemDef.shortname))
            {
                return;
            }

            var targets = new HashSet<ulong>();

            // 1. Get Team Members
            var team = RelationshipManager.ServerInstance.FindPlayersTeam(sharer.userID);
            if (team != null)
            {
                foreach (ulong memberId in team.members)
                {
                    if (memberId != sharer.userID)
                    {
                        targets.Add(memberId);
                    }
                }
            }

            // 2. Get Clan Members (Official Clan System)
            if (sharer.clanId != 0 && ClanManager.ServerInstance != null)
            {
                IClan clan = null;
                if (ClanManager.ServerInstance.Backend?.TryGet(sharer.clanId, out clan) ?? false)
                {
                    if (clan != null && clan.Members != null)
                    {
                        foreach (ClanMember member in clan.Members)
                        {
                            if (member.SteamId != sharer.userID)
                            {
                                targets.Add(member.SteamId);
                            }
                        }
                    }
                }
            }

            if (targets.Count == 0) return;

            // 3. Identify Blueprint IDs (Main item + any sub-items)
            var blueprintsToShare = new List<int> { itemDef.itemid };
            if (itemDef.Blueprint?.additionalUnlocks != null)
            {
                foreach (var subItem in itemDef.Blueprint.additionalUnlocks)
                    blueprintsToShare.Add(subItem.itemid);
            }

            int sharedCount = 0;

            // 4. Loop through targets
            foreach (ulong targetId in targets)
            {
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