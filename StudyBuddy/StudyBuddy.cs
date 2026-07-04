using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("StudyBuddy", "Orangemart", "1.0.6")]
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
            HandleTechTreeUnlock(workbench, nodeOrItemDef, player);
        }

        // Hook: Tech Tree (Post-Unlock)
        private void OnTechTreeNodeUnlocked(Workbench workbench, object nodeOrItemDef, BasePlayer player)
        {
            HandleTechTreeUnlock(workbench, nodeOrItemDef, player);
        }

        private void HandleTechTreeUnlock(Workbench workbench, object nodeOrItemDef, BasePlayer player)
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

            List<ItemDefinition> prerequisites = new List<ItemDefinition>();
            if (workbench != null)
            {
                object techTree = GetTechTree(workbench);
                if (techTree != null)
                {
                    object nodeInstance = FindNodeInstance(techTree, itemDef);
                    if (nodeInstance != null)
                    {
                        AddParentsFromNode(nodeInstance, techTree, new HashSet<object>(), prerequisites);
                    }
                }
            }

            TryShareBlueprint(itemDef, player, prerequisites);
        }

        private object GetTechTree(object workbench)
        {
            if (workbench == null) return null;
            var field = workbench.GetType().GetField("techTree", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        ?? workbench.GetType().GetField("TechTree", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null) return field.GetValue(workbench);

            var prop = workbench.GetType().GetProperty("techTree", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                       ?? workbench.GetType().GetProperty("TechTree", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (prop != null) return prop.GetValue(workbench);

            return null;
        }

        private object FindNodeInstance(object techTree, ItemDefinition itemDef)
        {
            if (techTree == null || itemDef == null) return null;

            var nodesField = techTree.GetType().GetField("nodes", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (nodesField == null) return null;

            var nodesEnumerable = nodesField.GetValue(techTree) as System.Collections.IEnumerable;
            if (nodesEnumerable == null) return null;

            foreach (object node in nodesEnumerable)
            {
                if (node == null) continue;
                if (GetItemDefFromNode(node) == itemDef)
                {
                    return node;
                }
            }

            return null;
        }

        private ItemDefinition GetItemDefFromNode(object node)
        {
            if (node == null) return null;
            ItemDefinition itemDef = node as ItemDefinition;
            if (itemDef != null) return itemDef;

            var fields = node.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(ItemDefinition))
                {
                    var val = field.GetValue(node) as ItemDefinition;
                    if (val != null) return val;
                }
            }

            var props = node.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.PropertyType == typeof(ItemDefinition))
                {
                    var val = prop.GetValue(node) as ItemDefinition;
                    if (val != null) return val;
                }
            }

            return null;
        }

        private int GetNodeId(object node)
        {
            if (node == null) return -1;
            var fields = node.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if ((field.Name.Equals("id", System.StringComparison.OrdinalIgnoreCase) || field.Name.Equals("nodeid", System.StringComparison.OrdinalIgnoreCase)) && field.FieldType == typeof(int))
                {
                    return (int)field.GetValue(node);
                }
            }

            var props = node.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if ((prop.Name.Equals("id", System.StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("nodeid", System.StringComparison.OrdinalIgnoreCase)) && prop.PropertyType == typeof(int))
                {
                    return (int)prop.GetValue(node);
                }
            }

            return -1;
        }

        private void AddParentsFromNode(object node, object techTree, HashSet<object> visitedNodes, List<ItemDefinition> result)
        {
            if (node == null || techTree == null || !visitedNodes.Add(node)) return;

            var nodesField = techTree.GetType().GetField("nodes", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Collections.IEnumerable allNodes = null;
            if (nodesField != null)
            {
                allNodes = nodesField.GetValue(techTree) as System.Collections.IEnumerable;
            }

            var fields = node.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType == typeof(ItemDefinition) || field.FieldType == typeof(int) || field.Name.Equals("id", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType))
                {
                    var val = field.GetValue(node);
                    if (val == null || val is string) continue;

                    if (val is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is int parentId)
                            {
                                object parentNode = FindNodeById(allNodes, parentId);
                                if (parentNode != null)
                                {
                                    ItemDefinition parentItem = GetItemDefFromNode(parentNode);
                                    if (parentItem != null && !result.Contains(parentItem))
                                    {
                                        result.Add(parentItem);
                                    }
                                    AddParentsFromNode(parentNode, techTree, visitedNodes, result);
                                }
                            }
                            else if (item != null && item.GetType() == node.GetType())
                            {
                                ItemDefinition parentItem = GetItemDefFromNode(item);
                                if (parentItem != null && !result.Contains(parentItem))
                                {
                                    result.Add(parentItem);
                                }
                                AddParentsFromNode(item, techTree, visitedNodes, result);
                            }
                        }
                    }
                }
                else if (field.FieldType == node.GetType())
                {
                    var val = field.GetValue(node);
                    if (val != null)
                    {
                        ItemDefinition parentItem = GetItemDefFromNode(val);
                        if (parentItem != null && !result.Contains(parentItem))
                        {
                            result.Add(parentItem);
                        }
                        AddParentsFromNode(val, techTree, visitedNodes, result);
                    }
                }
            }

            var props = node.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.PropertyType == typeof(ItemDefinition) || prop.PropertyType == typeof(int) || prop.Name.Equals("id", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (prop.GetIndexParameters().Length > 0) continue;

                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType))
                {
                    object val = null;
                    try { val = prop.GetValue(node); } catch {}
                    if (val == null || val is string) continue;

                    if (val is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is int parentId)
                            {
                                object parentNode = FindNodeById(allNodes, parentId);
                                if (parentNode != null)
                                {
                                    ItemDefinition parentItem = GetItemDefFromNode(parentNode);
                                    if (parentItem != null && !result.Contains(parentItem))
                                    {
                                        result.Add(parentItem);
                                    }
                                    AddParentsFromNode(parentNode, techTree, visitedNodes, result);
                                }
                            }
                            else if (item != null && item.GetType() == node.GetType())
                            {
                                ItemDefinition parentItem = GetItemDefFromNode(item);
                                if (parentItem != null && !result.Contains(parentItem))
                                {
                                    result.Add(parentItem);
                                }
                                AddParentsFromNode(item, techTree, visitedNodes, result);
                            }
                        }
                    }
                }
                else if (prop.PropertyType == node.GetType())
                {
                    object val = null;
                    try { val = prop.GetValue(node); } catch {}
                    if (val != null)
                    {
                        ItemDefinition parentItem = GetItemDefFromNode(val);
                        if (parentItem != null && !result.Contains(parentItem))
                        {
                            result.Add(parentItem);
                        }
                        AddParentsFromNode(val, techTree, visitedNodes, result);
                    }
                }
            }
        }

        private object FindNodeById(System.Collections.IEnumerable allNodes, int id)
        {
            if (allNodes == null || id < 0) return null;
            foreach (var node in allNodes)
            {
                if (node == null) continue;
                if (GetNodeId(node) == id)
                {
                    return node;
                }
            }
            return null;
        }

        #endregion

        #region Core Logic

        private void TryShareBlueprint(ItemDefinition itemDef, BasePlayer sharer, List<ItemDefinition> prerequisites = null)
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

            // 3. Identify Blueprint IDs (Main item + any sub-items + prerequisites)
            var blueprintsToShare = new List<int> { itemDef.itemid };
            if (itemDef.Blueprint?.additionalUnlocks != null)
            {
                foreach (var subItem in itemDef.Blueprint.additionalUnlocks)
                    blueprintsToShare.Add(subItem.itemid);
            }

            if (prerequisites != null)
            {
                foreach (var prereq in prerequisites)
                {
                    if (prereq != null && !blueprintsToShare.Contains(prereq.itemid))
                    {
                        // Only share prerequisite if the sharer actually has this blueprint unlocked
                        if (sharer.PersistantPlayerInfo != null && sharer.PersistantPlayerInfo.unlockedItems.Contains(prereq.itemid))
                        {
                            blueprintsToShare.Add(prereq.itemid);
                        }
                    }
                }
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