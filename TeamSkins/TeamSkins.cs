using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Team Skins", "Orangemart", "2.0.8")]
    [Description("Skin sharing system. Supports Redirects, Team Sharing, and Configurable Skins.")]
    public class TeamSkins : RustPlugin
    {
        [PluginReference] private Plugin PlayerDLCAPI;

        private const string PermUse = "teamskins.use";
        
        // --- Cache Data ---
        private readonly Dictionary<string, List<ulong>> _autoSkinCache = new Dictionary<string, List<ulong>>();
        private readonly Dictionary<string, List<SkinConfigEntry>> _manualSkinCache = new Dictionary<string, List<SkinConfigEntry>>();

        private readonly Dictionary<ulong, ItemContainer> _openContainers = new Dictionary<ulong, ItemContainer>();
        private readonly HashSet<ulong> _openingPlayers = new HashSet<ulong>();

        // --- Configuration ---
        private PluginConfig _config;

        private class PluginConfig
        {
            [JsonProperty("Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Commands = new List<string> { "skin", "skins", "sb" };

            [JsonProperty("Container Panel Name")]
            public string PanelName = "generic";

            [JsonProperty("Container Capacity")]
            public int Capacity = 36;

            [JsonProperty("Extra Skins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<SkinConfigEntry> ExtraSkins = new List<SkinConfigEntry>(); 
        }

        private class SkinConfigEntry
        {
            [JsonProperty("Item Shortname")]
            public string Shortname;

            [JsonProperty("Permission")]
            public string Permission;

            [JsonProperty("Skins")]
            public List<ulong> Skins;
        }

        #region Setup & Config

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig
            {
                ExtraSkins = new List<SkinConfigEntry>
                {
                    // Example provided for server admins to understand JSON structure
                    new SkinConfigEntry
                    {
                        Shortname = "hoodie",
                        Permission = "teamskins.admin",
                        Skins = new List<ulong> { 3492377614 }
                    }
                }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new System.Exception();
            }
            catch
            {
                PrintError("Configuration file is corrupt! Loading default config.");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);

            // Register Permissions from Config
            if (_config.ExtraSkins != null)
            {
                foreach (var entry in _config.ExtraSkins)
                {
                    if (!string.IsNullOrEmpty(entry.Permission))
                    {
                        if (!permission.PermissionExists(entry.Permission, this))
                            permission.RegisterPermission(entry.Permission, this);
                    }
                }
            }

            // Optimize Config Skins for Lookup
            BuildManualCache();
        }

        private Timer _initTimer;

        private void OnServerInitialized()
        {
            // Register Commands
            foreach (var cmd in _config.Commands)
            {
                AddCovalenceCommand(cmd, nameof(CmdSkin));
            }

            Puts($"[Team Skins] Registered {_config.Commands.Count} commands. Waiting for Steam Inventory...");

            // Check every 10 seconds, up to 12 times (2 minutes total)
            _initTimer = timer.Repeat(10f, 12, TryBuildCache);
        }

        private void TryBuildCache()
        {
            // Check if Steam definitions are actually populated
            var defs = Steamworks.SteamInventory.Definitions;
            int count = (defs != null) ? defs.Length : 0;

            // Rust usually has 5000+ skins. If we have less than 500, Steam hasn't finished loading.
            if (count > 500)
            {
                Puts($"[Team Skins] Steam Inventory detected ({count} items). Building Cache...");
                BuildUniversalCache();
                
                // Stop the timer so we don't keep building
                if (_initTimer != null && !_initTimer.Destroyed)
                {
                    _initTimer.Destroy();
                    _initTimer = null;
                }
            }
            else
            {
                Puts($"[Team Skins] Still waiting for skin definitions... (Found: {count})");
            }
        }

        private void Unload()
        {
            foreach (var kvp in _openContainers)
            {
                var player = BasePlayer.FindByID(kvp.Key);
                if (kvp.Value != null)
                {
                    if (player != null) player.GiveItem(kvp.Value.GetSlot(0));
                    kvp.Value.Kill();
                }
            }
            _openContainers.Clear();
            _openingPlayers.Clear();
        }

        #endregion

        #region Commands & Logic

        private void CmdSkin(IPlayer iplayer, string command, string[] args)
        {
            var player = iplayer.Object as BasePlayer;
            if (player == null) return;

            if (args.Length > 0 && args[0].ToLower() == "refresh" && player.IsAdmin)
            {
                player.ChatMessage("Rebuilding skin cache...");
                BuildUniversalCache();
                LoadConfig();
                BuildManualCache();
                return;
            }

            if (!iplayer.HasPermission(PermUse))
            {
                player.ChatMessage("You do not have permission (teamskins.use).");
                return;
            }

            _openingPlayers.Add(player.userID);
            if (player.inventory.loot.IsLooting()) player.EndLooting();
            
            timer.In(0.2f, () => OpenVirtualBox(player));
        }

        private void OpenVirtualBox(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
            {
                _openingPlayers.Remove(player.userID);
                return;
            }

            if (_openContainers.ContainsKey(player.userID))
            {
                var old = _openContainers[player.userID];
                if(old != null) old.Kill();
                _openContainers.Remove(player.userID);
            }

            var container = new ItemContainer();
            container.entityOwner = player;
            container.capacity = _config.Capacity;
            container.isServer = true;
            container.allowedContents = ItemContainer.ContentsType.Generic;
            container.GiveUID();
            
            _openContainers[player.userID] = container;

            player.inventory.loot.Clear();
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.entitySource = player;
            player.inventory.loot.itemSource = null;
            player.inventory.loot.AddContainer(container);
            player.inventory.loot.SendImmediate();
            
            player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", _config.PanelName);
            timer.In(1.0f, () => _openingPlayers.Remove(player.userID));
        }

        #endregion

        #region Interaction Hooks

        private object CanLootPlayer(BasePlayer looter, BasePlayer target)
        {
            if (looter != target) return null;
            if (_openContainers.ContainsKey(looter.userID)) return true;
            return null;
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            var player = inventory.GetComponent<BasePlayer>();
            if (player == null) return;
            if (_openingPlayers.Contains(player.userID)) return;

            if (_openContainers.ContainsKey(player.userID))
            {
                if (inventory.entitySource == player)
                {
                    var container = _openContainers[player.userID];
                    var item = container.GetSlot(0);
                    
                    // Give back the item in Slot 0 (if it exists)
                    if (item != null) player.GiveItem(item);

                    container.Kill();
                    _openContainers.Remove(player.userID);
                }
            }
        }

        // Fix: Visual Glitch - Clear ghosts if you drag the item out of Slot 0 safely
        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            var ownerId = _openContainers.FirstOrDefault(x => x.Value == container).Key;
            if (ownerId == 0) return;

            // If Slot 0 is now empty, wipe the skin preview immediately
            if (container.GetSlot(0) == null)
            {
                for (int i = 1; i < container.capacity; i++)
                {
                    var ghost = container.GetSlot(i);
                    if (ghost != null)
                    {
                        SafeRemoveItem(ghost);
                    }
                }
            }
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            var ownerId = _openContainers.FirstOrDefault(x => x.Value == container).Key;
            if (ownerId == 0) return;

            if (item.position == 0)
            {
                var player = BasePlayer.FindByID(ownerId);
                if (player != null) NextFrame(() => DisplaySkins(player, container, item));
            }
        }

        private void DisplaySkins(BasePlayer player, ItemContainer container, Item targetItem)
        {
            // Clear existing ghosts first safely
            for (int i = 1; i < container.capacity; i++)
            {
                var ghost = container.GetSlot(i);
                if (ghost != null) 
                { 
                    SafeRemoveItem(ghost); 
                }
            }

            var baseDef = GetBaseItemDef(targetItem.info);
            var skins = GetCombinedSkins(player, baseDef.shortname);

            if (skins.Count == 0)
            {
                player.ChatMessage("No skins found for this item.");
                return;
            }

            int slot = 1;
            foreach (var skinId in skins)
            {
                if (slot >= container.capacity) break;
                
                Item ghost = ItemManager.Create(baseDef, 1, skinId);
                if (ghost != null)
                {
                    ghost.condition = targetItem.condition;
                    ghost.maxCondition = targetItem.maxCondition;
                    
                    // --- AMMO EXPLOIT FIX ---
                    var projectile = ghost.GetHeldEntity() as BaseProjectile;
                    if (projectile != null && projectile.primaryMagazine != null)
                    {
                        projectile.primaryMagazine.contents = 0;
                    }
                    
                    ghost.MoveToContainer(container, slot);
                    slot++;
                }
            }
        }

        private object CanMoveItem(Item item, PlayerInventory playerLoot, ItemContainerId targetContainer, int targetSlot, int amount)
        {
            var player = playerLoot.GetComponent<BasePlayer>();
            if (player == null || !_openContainers.TryGetValue(player.userID, out var box)) return null;

            // Interaction: Clicking/Dragging a Skin Ghost
            if (item.parent == box && item.position > 0)
            {
                var originalItem = box.GetSlot(0);
                if (originalItem != null)
                {
                    bool isRedirect = item.info.shortname != originalItem.info.shortname;

                    NextFrame(() =>
                    {
                        if (originalItem == null || !originalItem.IsValid()) return;

                        if (isRedirect)
                        {
                            TransferItemProps(originalItem, item);
                            player.GiveItem(item);
                            originalItem.Remove();
                        }
                        else
                        {
                            originalItem.skin = item.skin;
                            originalItem.MarkDirty(); 

                            var heldEntity = originalItem.GetHeldEntity();
                            if (heldEntity != null)
                            {
                                heldEntity.skinID = item.skin;
                                heldEntity.SendNetworkUpdate();
                            }

                            player.GiveItem(originalItem);
                        }

                        player.ChatMessage($"Skin Applied! ({item.info.displayName.translated})");
                        
                        // FIX: Force the client to resync its inventory completely.
                        // This wipes out the fake predicted ghost item that causes the RPC kick.
                        player.inventory.SendSnapshot();
                        
                        if (player.IsConnected) player.EndLooting();
                    });

                    return false; 
                }
            }

            if (targetContainer == box.uid && targetSlot > 0) return false;

            return null;
        }

        // --- SPLITTING EXPLOIT FIX ---
        private void OnItemSplit(Item item, int amount)
        {
            if (item?.parent == null) return;

            // Check if the split happened inside one of our skin containers
            if (_openContainers.ContainsValue(item.parent))
            {
                var ownerId = _openContainers.FirstOrDefault(x => x.Value == item.parent).Key;
                var player = BasePlayer.FindByID(ownerId);
                
                if (player != null)
                {
                    // Move the split offshoot directly back to the player's inventory
                    NextFrame(() =>
                    {
                        if (item != null && item.IsValid())
                        {
                            player.GiveItem(item);
                        }
                    });
                }
            }
        }

        // --- SAFE REMOVE ITEM LOGIC ---
        private void SafeRemoveItem(Item item)
        {
            if (item == null) return;
            item.Remove(); 
        }

        private void TransferItemProps(Item source, Item destination)
        {
            destination.amount = source.amount;
            destination.condition = source.condition;
            destination.maxCondition = source.maxCondition;
            
            if (source.contents != null && destination.contents != null)
            {
                for (int i = source.contents.itemList.Count - 1; i >= 0; i--)
                {
                    var child = source.contents.itemList[i];
                    child.MoveToContainer(destination.contents);
                }
            }
            
            var sourceProjectile = source.GetHeldEntity() as BaseProjectile;
            var destProjectile = destination.GetHeldEntity() as BaseProjectile;
            
            if (sourceProjectile != null && destProjectile != null)
            {
                destProjectile.primaryMagazine.contents = sourceProjectile.primaryMagazine.contents;
                destProjectile.primaryMagazine.ammoType = sourceProjectile.primaryMagazine.ammoType;
            }
        }

        #endregion

        #region Cache & Lookup

        private void BuildManualCache()
        {
            _manualSkinCache.Clear();
            if (_config.ExtraSkins == null) return;

            foreach (var entry in _config.ExtraSkins)
            {
                if (string.IsNullOrEmpty(entry.Shortname)) continue;

                if (!_manualSkinCache.ContainsKey(entry.Shortname))
                    _manualSkinCache[entry.Shortname] = new List<SkinConfigEntry>();

                _manualSkinCache[entry.Shortname].Add(entry);
            }
        }

        private void BuildUniversalCache()
        {
            _autoSkinCache.Clear();

            // 1. Internal Scan
            foreach (var def in ItemManager.itemList)
            {
                if (def.skins != null)
                {
                    foreach (var skin in def.skins)
                    {
                        ulong skinId = (ulong)skin.id;
                        if (skinId == 0) continue;
                        var uiDef = def.isRedirectOf ?? def;
                        AddAutoCache(uiDef.shortname, skinId);
                    }
                }
            }

            // 2. Workshop Scan
            if (Steamworks.SteamInventory.Definitions != null)
            {
                foreach (var def in Steamworks.SteamInventory.Definitions)
                {
                    string shortname = def.GetProperty("itemshortname");
                    string workshopIdStr = def.GetProperty("workshopid");
                    if (string.IsNullOrEmpty(shortname)) continue;

                    ulong skinId = 0;
                    if (!string.IsNullOrEmpty(workshopIdStr) && ulong.TryParse(workshopIdStr, out ulong wId)) skinId = wId;
                    else skinId = (ulong)def.Id;

                    if (skinId != 0) AddAutoCache(shortname, skinId);
                }
            }
        }

        private void AddAutoCache(string shortname, ulong skinId)
        {
            if (!_autoSkinCache.ContainsKey(shortname)) _autoSkinCache[shortname] = new List<ulong>();
            if (!_autoSkinCache[shortname].Contains(skinId)) _autoSkinCache[shortname].Add(skinId);
        }

        private ItemDefinition GetBaseItemDef(ItemDefinition currentDef)
        {
            return currentDef.isRedirectOf ?? currentDef;
        }

        private List<ulong> GetCombinedSkins(BasePlayer player, string shortname)
        {
            var results = new HashSet<ulong>();

            // 1. Resolve Team Members
            var teamMembers = new List<ulong> { player.userID };
            if (player.currentTeam != 0)
            {
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team != null) teamMembers = team.members;
            }

            foreach (var memberId in teamMembers)
            {
                var member = BasePlayer.Find(memberId.ToString());
                if (member == null || !member.IsConnected) continue;

                // 2. Check Auto Cache (Steam/Game Ownership)
                if (_autoSkinCache.TryGetValue(shortname, out var autoSkins))
                {
                    foreach (var skinId in autoSkins)
                    {
                        if (HasSkinAccess(member, skinId)) results.Add(skinId);
                    }
                }

                // 3. Check Manual Config Skins (Permission)
                if (_manualSkinCache.TryGetValue(shortname, out var manualEntries))
                {
                    foreach (var entry in manualEntries)
                    {
                        // Check if member has permission for this set
                        if (string.IsNullOrEmpty(entry.Permission) || 
                            permission.UserHasPermission(member.UserIDString, entry.Permission))
                        {
                            foreach (var s in entry.Skins) results.Add(s);
                        }
                    }
                }
            }

            return results.ToList();
        }

        private bool HasSkinAccess(BasePlayer player, ulong skinId)
        {
            if (PlayerDLCAPI == null) return false;
            object result = PlayerDLCAPI.Call("IsOwnedOrFreeSkin", player, skinId);
            return result is bool b && b;
        }

        #endregion
    }
}