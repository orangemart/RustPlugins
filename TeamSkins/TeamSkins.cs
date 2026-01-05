using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Team Skins", "Orangemart", "1.2.1")]
    [Description("Allows team members to share their owned skins in the Skins menu.")]
    public class TeamSkins : RustPlugin
    {
        [PluginReference] private Plugin PlayerDLCAPI, Skins;

        // Cache <Shortname, List<WorkshopID>>
        private readonly Dictionary<string, List<ulong>> _itemSkinCache = new Dictionary<string, List<ulong>>();
        
        private int _retryCount = 0;

        private void OnServerInitialized()
        {
            CheckSteamDefinitions();
        }

        #region Steam Cache Building
        private void CheckSteamDefinitions()
        {
            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0)
            {
                _retryCount++;
                if (_retryCount < 10) 
                {
                    timer.In(6f, CheckSteamDefinitions);
                }
                else
                {
                    Puts("[TeamSkins] Warning: Steam Inventory Definitions timed out. Only built-in skins will be available.");
                    BuildSkinCache(); 
                }
                return;
            }
            BuildSkinCache();
        }

        private void BuildSkinCache()
        {
            _itemSkinCache.Clear();
            int builtInCount = 0;
            int workshopCount = 0;
            
            Puts("[TeamSkins] Starting Skin Cache Build...");

            foreach (var skin in ItemSkinDirectory.Instance.skins)
            {
                var def = skin.invItem?.itemDefinition;
                if (def == null)
                    def = ItemManager.FindItemDefinition(skin.itemid);

                if (def != null)
                {
                    AddToCache(def.shortname, (ulong)skin.id);
                    
                    var itemSkin = skin.invItem as ItemSkin;
                    if (itemSkin != null && itemSkin.workshopID != 0)
                    {
                        AddToCache(def.shortname, itemSkin.workshopID);
                    }
                    builtInCount++;
                }
            }

            if (Steamworks.SteamInventory.Definitions != null)
            {
                foreach (var def in Steamworks.SteamInventory.Definitions)
                {
                    string targetShortname = def.GetProperty("itemshortname");
                    string workshopIdStr = def.GetProperty("workshopid");

                    if (string.IsNullOrEmpty(targetShortname)) continue;

                    ulong skinId;
                    if (!string.IsNullOrEmpty(workshopIdStr) && ulong.TryParse(workshopIdStr, out ulong wId))
                        skinId = wId;
                    else 
                        skinId = (ulong)def.Id;

                    if (skinId != 0)
                    {
                        AddToCache(targetShortname, skinId);
                        workshopCount++;
                    }
                }
            }
            
            Puts($"[TeamSkins] Cache Complete. Built-in: {builtInCount}, Workshop/Steam: {workshopCount}. Total Items Tracked: {_itemSkinCache.Count}");
        }

        private void AddToCache(string shortname, ulong skinId)
        {
            if (!_itemSkinCache.ContainsKey(shortname))
                _itemSkinCache[shortname] = new List<ulong>();

            if (!_itemSkinCache[shortname].Contains(skinId))
                _itemSkinCache[shortname].Add(skinId);
        }
        #endregion

        #region Team & Connection Watchers (Cache Clearing)
        
        // Helper to ask Skins plugin to clear cache for a player
        private void InvalidateSkinsCache(ulong userId)
        {
            if (Skins != null && Skins.IsLoaded)
            {
                // Calling "PurgeCache" with the userID and null (to clear ALL items for that user)
                Skins.Call("PurgeCache", userId, null);
            }
        }

        // When a player logs in, their teammates might now have access to new skins.
        // We must invalidate the cache for the player AND their teammates.
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player.currentTeam == 0) return;
            
            InvalidateSkinsCache(player.userID); // Clear their own cache
            
            var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
            if (team != null)
            {
                foreach (var memberId in team.members)
                {
                    if (memberId != player.userID) InvalidateSkinsCache(memberId);
                }
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            // If a player leaves, their skins are no longer available. Clear teammate caches.
            if (player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null)
                {
                    foreach (var memberId in team.members)
                    {
                        InvalidateSkinsCache(memberId);
                    }
                }
            }
        }

        private void OnTeamAcceptInvite(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            // Player joined a team -> Clear cache for everyone in that team so they see new skins
            foreach (var memberId in team.members) InvalidateSkinsCache(memberId);
        }

        private void OnTeamLeave(RelationshipManager.PlayerTeam team, BasePlayer player)
        {
            // Player left -> Clear their cache (lose access) and teammates cache (lose their access)
            InvalidateSkinsCache(player.userID);
            foreach (var memberId in team.members) InvalidateSkinsCache(memberId);
        }

        private void OnTeamKick(RelationshipManager.PlayerTeam team, BasePlayer player, ulong target)
        {
            InvalidateSkinsCache(target);
            foreach (var memberId in team.members) InvalidateSkinsCache(memberId);
        }

        private void OnTeamDisband(RelationshipManager.PlayerTeam team)
        {
            foreach (var memberId in team.members) InvalidateSkinsCache(memberId);
        }
        
        #endregion

        #region Core Logic
        private void OnSkinsFetch(BasePlayer player, ItemDefinition info, List<ulong> skins)
        {
            if (player == null || info == null) return;
            if (PlayerDLCAPI == null || !PlayerDLCAPI.IsLoaded) return;
            if (player.currentTeam == 0) return;

            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
            if (team == null) return;

            if (!_itemSkinCache.ContainsKey(info.shortname)) return;

            List<ulong> potentialSkins = _itemSkinCache[info.shortname];
            int addedCount = 0;

            foreach (var memberId in team.members)
            {
                // Modification: We no longer skip the player themselves.
                // We also optimize: if the member is the current player, use the existing object 
                // to avoid an unnecessary helper search.
                BasePlayer memberPlayer;
                
                if (memberId == player.userID)
                {
                    memberPlayer = player;
                }
                else
                {
                    memberPlayer = BasePlayer.Find(memberId.ToString());
                }

                if (memberPlayer == null || !memberPlayer.IsConnected) continue;

                foreach (ulong skinId in potentialSkins)
                {
                    if (skins.Contains(skinId)) continue;

                    bool isOwned = CallDlcApi(memberPlayer, skinId);

                    if (isOwned)
                    {
                        skins.Add(skinId);
                        addedCount++;
                    }
                }
            }
            
            if (addedCount > 0)
            {
               // Puts($"[TeamSkins] Added {addedCount} shared skins for {player.displayName} ({info.shortname}).");
            }
        }

        private bool CallDlcApi(BasePlayer player, ulong skinId)
        {
            object result = PlayerDLCAPI.Call("IsOwnedOrFreeSkin", player, skinId);
            return result is bool hasSkin && hasSkin;
        }
        #endregion
    }
}