using System;
using System.Collections.Generic;
using System.Reflection;
using Oxide.Core;
using UnityEngine;
using Facepunch; // Pool

namespace Oxide.Plugins
{
    [Info("Miner", "Orangemart", "1.8.0")]
    [Description("Custom-skinned fridges generate scrap while sufficiently powered. /miner.craft with cost, per-player limits, VIP limits, and wipe command. Lang-based messages.")]
    public class Miner : RustPlugin
    {
        #region Config

        private PluginConfig _config;

        public class PluginConfig
        {
            // Core targeting
            public string FridgePrefabShortname = "fridge.deployed";
            public ulong  TargetSkinId = 1375523896;

            // Power & production
            public int    RequiredPower = 100;        // minimum adjusted input power to produce
            public int    InputCompensationWatts = 5; // adds back entity self-draw when estimating input
            public int    ScrapPerTick = 21;
            public float  IntervalMinutes = 60f;

            // Inventory behavior
            public int    MaxFridgeSlotsToUse = 48;

            // Crafting & item presentation
            public bool   CraftEnabled = true;                 // enable /miner.craft
            public bool   CraftPermissionRequired = false;     // require a permission to craft
            public string CraftPermission = "miner.craft";     // permission string (if enabled)
            public string MinerItemShortname = "fridge";       // placeable item to give
            public string MinerItemDisplayName = "Miner";      // custom item name
            public Dictionary<string, int> CraftCost = new Dictionary<string, int>(); // EMPTY by default

            // Craft limiting (per-player)
            public bool   CraftLimitEnabled = true;  // toggle per-player total craft limit
            public int    CraftMaxPerPlayer = 1;     // total crafts for regular players (0 = unlimited)

            // VIP limiting
            public string VipPermission = "miner.vip"; // players with this get a separate limit
            public int    VipCraftMaxPerPlayer = 2;    // total crafts for VIPs (0 = unlimited)

            // Debug
            public bool   LogDebug = false;
        }

        protected override void LoadDefaultConfig()
{
    _config = new PluginConfig
    {
        FridgePrefabShortname = "fridge.deployed",
        TargetSkinId = 1375523896,
        RequiredPower = 100,
        InputCompensationWatts = 5,
        ScrapPerTick = 21,
        IntervalMinutes = 60f,
        MaxFridgeSlotsToUse = 48,
        CraftEnabled = true,
        CraftPermissionRequired = false,
        CraftPermission = "miner.craft",
        MinerItemShortname = "fridge",
        MinerItemDisplayName = "Miner",
        CraftLimitEnabled = true,
        CraftMaxPerPlayer = 1,
        VipPermission = "miner.vip",
        VipCraftMaxPerPlayer = 2,
        LogDebug = false,
        CraftCost = new Dictionary<string, int>   // defaults used ONLY when creating a fresh config
        {
            { "scrap", 500 },
            { "metal.fragments", 2500 },
            { "gears", 10 }
        }
    };
    SaveConfig();
}


        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>() ?? new PluginConfig();
            }
            catch (Exception e)
            {
                PrintError($"Config error: {e.Message}. Loading defaults.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                // Crafting
                ["Craft.Disabled"] = "Crafting is disabled.",
                ["Craft.NoPermission"] = "You don't have permission to craft a Miner.",
                ["Craft.NotEnoughHeader"] = "Not enough resources to craft Miner:",
                ["Craft.NotEnoughLine"] = " - {0} x{1}",
                ["Craft.Success"] = "You crafted a Miner! Requires {0}w to run.",
                ["Craft.SuccessRemaining"] = "You crafted a Miner! You can craft {0} more. Requires {1}w to run.",
                ["Craft.LimitReached"] = "You have reached your Miner craft limit ({0}).",

                // Admin / utility
                ["Admin.WipeDone"] = "Miner craft counts wiped.",
                ["Admin.Reloaded"] = "[Miner] Config reloaded.",
                ["Scan.Result"] = "Target fridges present: {0}",

                // Generic
                ["Error.CreateItem"] = "Failed to craft Miner (item create returned null)."
            }, this);
        }

        private string L(string key, BasePlayer player = null) =>
            lang.GetMessage(key, this, player?.UserIDString);

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null) return;
            var fmt = L(key, player);
            try { player.ChatMessage(string.Format(fmt, args)); }
            catch { player.ChatMessage(fmt); }
        }

        #endregion

        #region Permissions & Data

        private const string DataFileName = "Miner.Craft";
        private PersistData _data;

        private class PersistData
        {
            public Dictionary<ulong, int> CraftedByUserID = new Dictionary<ulong, int>();
        }

        private void Init()
        {
            if (!string.IsNullOrWhiteSpace(_config.CraftPermission))
                permission.RegisterPermission(_config.CraftPermission, this);
            if (!string.IsNullOrWhiteSpace(_config.VipPermission))
                permission.RegisterPermission(_config.VipPermission, this);

            LoadData();
        }

        private void LoadData()
        {
            try
            {
                _data = Interface.Oxide.DataFileSystem.ReadObject<PersistData>(DataFileName) ?? new PersistData();
            }
            catch { _data = new PersistData(); }
        }

        private void SaveData()
        {
            try { Interface.Oxide.DataFileSystem.WriteObject(DataFileName, _data); }
            catch (Exception e) { PrintError($"Failed to save data: {e.Message}"); }
        }

        private int GetCraftCount(ulong userId)
        {
            return _data.CraftedByUserID.TryGetValue(userId, out var c) ? c : 0;
        }

        private void IncCraftCount(ulong userId)
        {
            _data.CraftedByUserID[userId] = GetCraftCount(userId) + 1;
            SaveData();
        }

        private int GetPlayerMaxCrafts(BasePlayer player)
        {
            if (player != null &&
                !string.IsNullOrWhiteSpace(_config.VipPermission) &&
                permission.UserHasPermission(player.UserIDString, _config.VipPermission))
            {
                return _config.VipCraftMaxPerPlayer; // VIP path
            }
            return _config.CraftMaxPerPlayer; // regular path
        }

        #endregion

        #region State

        private readonly HashSet<BaseEntity> _tracked = new HashSet<BaseEntity>();
        private readonly Dictionary<ulong, ItemContainer> _containerCache = new Dictionary<ulong, ItemContainer>();
        private readonly HashSet<ItemContainer> _trackedContainers = new HashSet<ItemContainer>();
        private Timer _tickTimer;

        #endregion

        #region Hooks — lifecycle

        private void OnServerInitialized()
        {
            int found = 0;
            foreach (var net in BaseNetworkable.serverEntities)
            {
                var be = net as BaseEntity;
                if (be == null) continue;
                if (IsTargetFridge(be))
                {
                    found++;
                    TrackFridge(be);
                    TrySetEntityDisplayName(be, _config.MinerItemDisplayName);
                }
            }
            if (_config.LogDebug) Puts($"Initialized. Found {found} target fridges.");
            StartProductionTimer();
        }

        private void Unload()
        {
            _tracked.Clear();
            _trackedContainers.Clear();
            _containerCache.Clear();
            _tickTimer?.Destroy();
            SaveData();
        }

        private void OnServerSave() => SaveData();

        #endregion

        #region Hooks — spawn/despawn

        private void OnEntitySpawned(BaseNetworkable net)
        {
            var be = net as BaseEntity;
            if (be == null) return;

            NextFrame(() =>
            {
                if (be == null || be.IsDestroyed) return;

                if (IsTargetFridge(be))
                {
                    TrackFridge(be);
                    TrySetEntityDisplayName(be, _config.MinerItemDisplayName);
                }
            });
        }

        private void OnEntityKill(BaseNetworkable net)
        {
            var be = net as BaseEntity;
            if (be == null) return;

            _tracked.Remove(be);
            if (be.net != null)
            {
                if (_containerCache.TryGetValue(be.net.ID.Value, out var cont))
                    _trackedContainers.Remove(cont);
                _containerCache.Remove(be.net.ID.Value);
            }
        }

        #endregion

        #region Chat / Console — crafting & wipe

        [ChatCommand("miner.craft")]
        private void CmdMinerCraft(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;

            if (!_config.CraftEnabled)
            {
                Reply(player, "Craft.Disabled");
                return;
            }

            if (_config.CraftPermissionRequired && !_HasCraftPerm(player))
            {
                Reply(player, "Craft.NoPermission");
                return;
            }

            // Limit check
            if (_config.CraftLimitEnabled)
            {
                int limit = GetPlayerMaxCrafts(player);
                if (limit > 0)
                {
                    int used = GetCraftCount(player.userID);
                    if (used >= limit)
                    {
                        Reply(player, "Craft.LimitReached", limit);
                        return;
                    }
                }
            }

            // Validate cost & inventory
            var shortfalls = Pool.GetList<string>();
            foreach (var kvp in _config.CraftCost)
            {
                var shortname = kvp.Key;
                var need = Mathf.Max(0, kvp.Value);
                if (need == 0) continue;

                var def = ItemManager.FindItemDefinition(shortname);
                if (def == null)
                {
                    shortfalls.Add(string.Format(L("Craft.NotEnoughLine", player), shortname, $"{need} (unknown item)"));
                    continue;
                }

                var have = player.inventory.GetAmount(def.itemid);
                if (have < need)
                {
                    shortfalls.Add(string.Format(L("Craft.NotEnoughLine", player), shortname, need - have));
                }
            }

            if (shortfalls.Count > 0)
            {
                player.ChatMessage(L("Craft.NotEnoughHeader", player) + "\n" + string.Join("\n", shortfalls.ToArray()));
                Pool.FreeList(ref shortfalls);
                return;
            }
            Pool.FreeList(ref shortfalls);

            // Take cost
            foreach (var kvp in _config.CraftCost)
            {
                var need = Mathf.Max(0, kvp.Value);
                if (need == 0) continue;

                var def = ItemManager.FindItemDefinition(kvp.Key);
                if (def != null)
                    player.inventory.Take(null, def.itemid, need);
            }

            // Give Miner item
            var item = CreateMinerItem();
            if (item == null)
            {
                Reply(player, "Error.CreateItem");
                return;
            }
            player.GiveItem(item);

            // Record craft & message
            int required = Mathf.Max(0, _config.RequiredPower);
            if (_config.CraftLimitEnabled)
            {
                int limit = GetPlayerMaxCrafts(player);
                if (limit > 0)
                {
                    IncCraftCount(player.userID);
                    int used = GetCraftCount(player.userID);
                    int remain = Mathf.Max(0, limit - used);
                    if (remain > 0) Reply(player, "Craft.SuccessRemaining", remain, required);
                    else Reply(player, "Craft.Success", required); // reached limit exactly after this craft
                    return;
                }
            }
            Reply(player, "Craft.Success", required);
        }

        // Wipe command (chat): resets all player craft counts
        [ChatCommand("miner_wipe")]
        private void CmdMinerWipeChat(BasePlayer player, string cmd, string[] args)
        {
            if (player != null && !player.IsAdmin) return;
            DoWipeCounts();
            Reply(player, "Admin.WipeDone");
            Puts("[Miner] Craft counts wiped by chat command.");
        }

        // Wipe command (console/RCON)
        [ConsoleCommand("miner_wipe")]
        private void CmdMinerWipeConsole(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && arg.Connection.authLevel < 2) return; // in-game console: admin only
            DoWipeCounts();
            Puts("[Miner] Craft counts wiped by console command.");
        }

        private void DoWipeCounts()
        {
            _data.CraftedByUserID.Clear();
            SaveData();
        }

        private bool _HasCraftPerm(BasePlayer player)
        {
            if (string.IsNullOrWhiteSpace(_config.CraftPermission))
                return true;
            return permission.UserHasPermission(player.UserIDString, _config.CraftPermission);
        }

        private Item CreateMinerItem()
        {
            var shortname = string.IsNullOrWhiteSpace(_config.MinerItemShortname) ? "fridge" : _config.MinerItemShortname;
            Item item = null;
            try
            {
                item = ItemManager.CreateByName(shortname, 1, _config.TargetSkinId);
                if (item != null && !string.IsNullOrWhiteSpace(_config.MinerItemDisplayName))
                    item.name = _config.MinerItemDisplayName;
            }
            catch (Exception e)
            {
                PrintError($"CreateMinerItem error: {e}");
            }
            return item;
        }

        #endregion

        #region Scrap production core

        private bool IsTargetFridge(BaseEntity be)
        {
            if (be == null) return false;
            if (!string.Equals(be.ShortPrefabName, _config.FridgePrefabShortname, StringComparison.Ordinal))
                return false;
            return be.skinID == _config.TargetSkinId;
        }

        private void TrackFridge(BaseEntity be)
        {
            _tracked.Add(be);
            if (_config.LogDebug)
                Puts($"[Debug] Tracking fridge {be.net?.ID.Value} (skin {be.skinID}).");

            // Warm container cache & register for CanAcceptItem
            var cont = FindFridgeContainer(be);
            if (cont != null && be.net != null)
            {
                _containerCache[be.net.ID.Value] = cont;
                _trackedContainers.Add(cont);
            }
        }

        private void StartProductionTimer()
        {
            _tickTimer?.Destroy();
            var seconds = Mathf.Max(1f, _config.IntervalMinutes * 60f);
            _tickTimer = timer.Every(seconds, ProduceTick);
        }

        private void ProduceTick()
        {
            if (_tracked.Count == 0) return;

            var toRemove = Pool.GetList<BaseEntity>();

            foreach (var be in _tracked)
            {
                if (be == null || be.IsDestroyed) { toRemove.Add(be); continue; }

                var io = be as IOEntity;
                if (io == null)
                {
                    if (_config.LogDebug) Puts($"[Debug] {be.net?.ID.Value} not IOEntity.");
                    continue;
                }

                int energy = GetInputEnergy(io);
                if (energy < _config.RequiredPower)
                {
                    if (_config.LogDebug)
                        Puts($"[Debug] Fridge {be.net?.ID.Value} insufficient power {energy}/{_config.RequiredPower}; skipping.");
                    continue;
                }

                TryAddScrap(be, _config.ScrapPerTick);
            }

            foreach (var dead in toRemove) _tracked.Remove(dead);
            Pool.FreeList(ref toRemove);
        }

        // Version-tolerant power read (usually post-consumption passthrough)
        private int GetInputEnergy(IOEntity io)
        {
            int measured;
            try { measured = io.GetCurrentEnergy(); }
            catch
            {
                try { measured = io.GetPassthroughAmount(0); }
                catch { measured = io.IsPowered() ? 1 : 0; }
            }

            var adjusted = measured + Mathf.Max(0, _config.InputCompensationWatts);
            if (_config.LogDebug)
                Puts($"[Debug] Measured={measured}w, +comp={_config.InputCompensationWatts} => adjusted={adjusted}w");
            return adjusted;
        }

        #endregion

        #region Inventory discovery & scrap insertion

        // Allow scrap into our fridge containers (vanilla fridges reject scrap)
        private object CanAcceptItem(ItemContainer container, Item item, int targetPos)
        {
            try
            {
                if (container == null || item == null) return null;
                if (item.info?.shortname != "scrap") return null;

                if (_trackedContainers.Contains(container))
                {
                    if (_config.LogDebug) Puts("[Hook] CanAcceptItem(container,item,int): allowing scrap.");
                    return true;
                }
            }
            catch { }
            return null;
        }

        // 2-arg variant for other Oxide builds
        private object CanAcceptItem(ItemContainer container, Item item)
        {
            try
            {
                if (container == null || item == null) return null;
                if (item.info?.shortname != "scrap") return null;

                if (_trackedContainers.Contains(container))
                {
                    if (_config.LogDebug) Puts("[Hook] CanAcceptItem(container,item): allowing scrap.");
                    return true;
                }
            }
            catch { }
            return null;
        }

        private ItemContainer GetFridgeContainer(BaseEntity be)
        {
            if (be == null || be.IsDestroyed || be.net == null) return null;

            var id = be.net.ID.Value;
            if (_containerCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            var found = FindFridgeContainer(be);
            if (found != null)
            {
                _containerCache[id] = found;
                _trackedContainers.Add(found);
            }
            return found;
        }

        private ItemContainer FindFridgeContainer(BaseEntity be)
        {
            // 1) If it does inherit StorageContainer
            var sc = be as StorageContainer;
            if (sc != null && sc.inventory != null) return sc.inventory;

            // 2) Try component on self
            sc = be.GetComponent<StorageContainer>();
            if (sc != null && sc.inventory != null) return sc.inventory;

            // 3) Search children
            var allSC = be.GetComponentsInChildren<StorageContainer>(true);
            if (allSC != null)
                foreach (var s in allSC)
                    if (s != null && s.inventory != null) return s.inventory;

            // 4) Reflect ItemContainer from the entity itself
            var ic = FindItemContainerViaReflection(be);
            if (ic != null) return ic;

            // 5) Reflect through all components in children
            var comps = be.GetComponentsInChildren<Component>(true);
            foreach (var c in comps)
            {
                ic = FindItemContainerViaReflection(c);
                if (ic != null) return ic;
            }

            if (_config.LogDebug)
                Puts($"[Debug] Inventory not found for {be.net?.ID.Value} (Type '{be.GetType().Name}', Prefab '{be.PrefabName}')");
            return null;
        }

        private ItemContainer FindItemContainerViaReflection(object obj)
        {
            if (obj == null) return null;
            var t = obj.GetType();

            // fields
            var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            foreach (var f in fields)
            {
                try
                {
                    if (typeof(ItemContainer).IsAssignableFrom(f.FieldType))
                    {
                        var v = f.GetValue(obj) as ItemContainer;
                        if (v != null) return v;
                    }
                }
                catch { }
            }

            // properties
            var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            foreach (var p in props)
            {
                try
                {
                    if (!p.CanRead) continue;
                    if (typeof(ItemContainer).IsAssignableFrom(p.PropertyType))
                    {
                        var v = p.GetValue(obj, null) as ItemContainer;
                        if (v != null) return v;
                    }
                }
                catch { }
            }

            return null;
        }

        private void TryAddScrap(BaseEntity be, int amount)
        {
            if (amount <= 0 || be == null || be.IsDestroyed) return;

            var container = GetFridgeContainer(be);
            if (container == null)
            {
                if (_config.LogDebug)
                    Puts($"[Debug] Fridge {be.net?.ID.Value} inventory not found. Prefab='{be.PrefabName}', Short='{be.ShortPrefabName}', Type='{be.GetType().Name}'");
                return;
            }

            var scrapDef = ItemManager.FindItemDefinition("scrap");
            if (scrapDef == null)
            {
                PrintError("Could not find item definition for 'scrap'.");
                return;
            }

            int maxSlots = Mathf.Max(1, _config.MaxFridgeSlotsToUse);
            int remaining = amount;

            // 1) Top-up existing scrap stacks first
            for (int i = 0; i < container.itemList.Count && remaining > 0 && i < maxSlots; i++)
            {
                var it = container.itemList[i];
                if (it == null || it.info != scrapDef) continue;

                int space = it.MaxStackable() - it.amount;
                if (space <= 0) continue;

                int add = Mathf.Min(space, remaining);
                it.amount += add;
                it.MarkDirty();
                remaining -= add;
            }

            // 2) Create new stacks if needed (and if there’s room)
            while (remaining > 0 && container.itemList.Count < container.capacity)
            {
                int toCreate = Mathf.Min(remaining, scrapDef.stackable);
                var newItem = ItemManager.Create(scrapDef, toCreate);
                if (newItem == null) break;

                bool ok = newItem.MoveToContainer(container);
                if (!ok)
                {
                    // Fallback: force-insert (bypass filters) to keep production reliable
                    try
                    {
                        newItem.parent = container;
                        newItem.MarkDirty();
                        if (!container.itemList.Contains(newItem))
                            container.itemList.Add(newItem);
                        container.MarkDirty();
                        ok = true;
                        if (_config.LogDebug)
                            Puts($"[Debug] Forced Insert scrap into fridge {be.net?.ID.Value} (bypassed filter).");
                    }
                    catch (Exception ex)
                    {
                        if (_config.LogDebug)
                            Puts($"[Debug] Forced Insert failed: {ex.Message}");
                        newItem.Remove();
                        break;
                    }
                }

                if (!ok)
                {
                    newItem.Remove();
                    if (_config.LogDebug)
                        Puts($"[Debug] MoveToContainer rejected scrap for fridge {be.net?.ID.Value}.");
                    break;
                }

                remaining -= toCreate;
            }

            if (_config.LogDebug)
                Puts($"[Debug] Fridge {be.net?.ID.Value} produced {amount - remaining} scrap (requested {amount}).");
        }

        #endregion

        #region Helpers

        private void TrySetEntityDisplayName(BaseEntity be, string display)
        {
            try
            {
                if (be == null || be.IsDestroyed) return;
                if (!string.IsNullOrWhiteSpace(display))
                {
                    be._name = display;           // many deployables respect this
                    be.SendNetworkUpdate();
                }
            }
            catch { }
        }

        #endregion

        #region Admin Commands

        [ChatCommand("fsg.reload")]
        private void CmdReload(BasePlayer player, string cmd, string[] args)
        {
            if (player != null && !player.IsAdmin) return;

            LoadConfig();
            SaveConfig();

            StartProductionTimer();
            Reply(player, "Admin.Reloaded");
        }

        [ChatCommand("fsg.scan")]
        private void CmdScan(BasePlayer player, string cmd, string[] args)
        {
            if (player != null && !player.IsAdmin) return;

            int count = 0;
            foreach (var net in BaseNetworkable.serverEntities)
            {
                var be = net as BaseEntity;
                if (be != null && IsTargetFridge(be)) count++;
            }
            Reply(player, "Scan.Result", count);
        }

        #endregion
    }
}
