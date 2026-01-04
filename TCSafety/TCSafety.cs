using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("TCSafety", "Orangemart", "1.0.1")]
    [Description("Enforces TC placement rules and auto-deploys key locks.")]
    public class TCSafety : RustPlugin
    {
        #region Configuration

        private PluginConfig config;

        private class PluginConfig
        {
            public bool RequireFoundation { get; set; }
            public bool BanTwigFoundation { get; set; }
            public bool AutoLock { get; set; }
            public bool RefundItemOnBlock { get; set; }

            public static PluginConfig DefaultConfig()
            {
                return new PluginConfig
                {
                    RequireFoundation = true,
                    BanTwigFoundation = true,
                    AutoLock = true,
                    RefundItemOnBlock = true
                };
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = PluginConfig.DefaultConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null)
                {
                    LoadDefaultConfig();
                }
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Error_NotFoundation"] = "Tool Cupboards must be placed on a foundation.",
                ["Error_TwigFound"] = "Tool Cupboards cannot be placed on Twig foundations. Upgrade the foundation first.",
                ["Error_Generic"] = "Invalid Tool Cupboard placement."
            }, this);
        }

        #endregion

        #region Hooks

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (plan == null || go == null) return;

            BaseEntity entity = go.GetComponent<BaseEntity>();
            if (entity == null) return;

            // Check if the entity is a Tool Cupboard
            // "cupboard.tool.deployed" is the standard prefab name for a TC
            if (!entity.ShortPrefabName.Contains("cupboard.tool")) return;

            BasePlayer player = plan.GetOwnerPlayer();
            
            // 1. Validation Checks
            if (config.RequireFoundation || config.BanTwigFoundation)
            {
                if (!ValidatePlacement(entity, player))
                {
                    // If validation fails, ValidatePlacement handles the destruction and messaging
                    return;
                }
            }

            // 2. Auto Lock Logic
            if (config.AutoLock)
            {
                // We use NextFrame to ensure the TC is fully initialized in the world before attaching
                NextFrame(() =>
                {
                    if (entity == null || entity.IsDestroyed) return;
                    AddKeyLock(entity, player);
                });
            }
        }

        #endregion

        #region Helpers

        private bool ValidatePlacement(BaseEntity tc, BasePlayer player)
        {
            // Raycast down from the TC to find what it is sitting on
            RaycastHit hit;
            // Lift origin up slightly (0.1f) and cast down. 
            // Mask for Construction (Foundations/Floors)
            if (Physics.Raycast(tc.transform.position + new Vector3(0, 0.1f, 0), Vector3.down, out hit, 1.0f, LayerMask.GetMask("Construction")))
            {
                BaseEntity hitEntity = hit.GetEntity();
                BuildingBlock block = hitEntity as BuildingBlock;

                if (block != null)
                {
                    // Feature 1: Check if it is a foundation (Square or Triangle)
                    if (config.RequireFoundation)
                    {
                        if (!block.ShortPrefabName.Contains("foundation"))
                        {
                            RejectPlacement(tc, player, "Error_NotFoundation");
                            return false;
                        }
                    }

                    // Feature 2: Check if it is Twig
                    if (config.BanTwigFoundation)
                    {
                        if (block.grade == BuildingGrade.Enum.Twigs)
                        {
                            RejectPlacement(tc, player, "Error_TwigFound");
                            return false;
                        }
                    }
                    
                    // Valid placement
                    return true;
                }
            }

            // If we didn't hit a building block but RequireFoundation is on
            if (config.RequireFoundation)
            {
                RejectPlacement(tc, player, "Error_NotFoundation");
                return false;
            }

            return true;
        }

        private void RejectPlacement(BaseEntity tc, BasePlayer player, string langKey)
        {
            // Notify player
            if (player != null)
            {
                player.ChatMessage(lang.GetMessage(langKey, this, player.UserIDString));
                
                // Refund item
                if (config.RefundItemOnBlock)
                {
                    player.GiveItem(ItemManager.CreateByName("cupboard.tool", 1));
                }
            }

            // Destroy the invalid TC immediately
            tc.Kill();
        }

        private void AddKeyLock(BaseEntity tc, BasePlayer player)
{
    string lockPrefab = "assets/prefabs/locks/keylock/lock.key.prefab";

    BaseEntity lockEntity = GameManager.server.CreateEntity(lockPrefab, Vector3.zero, Quaternion.identity);
    
    if (lockEntity == null) return;

    // 1. Set Parent (Visual attachment)
    lockEntity.SetParent(tc, tc.GetSlotAnchorName(BaseEntity.Slot.Lock));

    // 2. Set Owner
    if (player != null)
    {
        lockEntity.OwnerID = player.userID;
    }
    
    // 3. Spawn
    lockEntity.Spawn();

    // 4. CRITICAL FIX: Explicitly set the TC's lock slot to this entity
    // This ensures tc.IsLocked() returns true.
    tc.SetSlot(BaseEntity.Slot.Lock, lockEntity);

    // 5. Lock it
    lockEntity.SetFlag(BaseEntity.Flags.Locked, true);

    Effect.server.Run("assets/bundled/prefabs/fx/build/deploy.prefab", tc.transform.position);
}
        #endregion
    }
}