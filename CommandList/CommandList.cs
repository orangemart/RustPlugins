using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CommandList", "Orangemart", "1.1.6")]
    [Description("Displays a clean UI list of server commands and descriptions in two columns.")]
    public class CommandList : CovalencePlugin
    {
        private Configuration config;
        private const string UIPanelName = "CommandListUI";

        private class Configuration
        {
            [JsonProperty("Commands List (Command : Description)")]
            public Dictionary<string, string> Commands { get; set; } = new Dictionary<string, string>
            {
                { "/commands", "Shows this help menu." }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new System.Exception();
            }
            catch
            {
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new default configuration for CommandList.");
            config = new Configuration();
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        [Command("commands", "help")]
        private void CmdShowCommands(IPlayer player, string command, string[] args)
        {
            var rustPlayer = player.Object as BasePlayer;
            if (rustPlayer == null) return;

            DrawCommandsUI(rustPlayer);
        }

        [Command("commands_close")]
        private void CmdCloseCommands(IPlayer player, string command, string[] args)
        {
            var rustPlayer = player.Object as BasePlayer;
            if (rustPlayer == null) return;
            
            CuiHelper.DestroyUi(rustPlayer, UIPanelName);
        }

        private void DrawCommandsUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UIPanelName);

            var elements = new CuiElementContainer();

            elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.85", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = "0.15 0.05", AnchorMax = "0.85 0.9" },
                CursorEnabled = true
            }, "Overlay", UIPanelName);

            // Title
            elements.Add(new CuiLabel
            {
                Text = { Text = "📖 Server Commands", FontSize = 24, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 0.98" }
            }, UIPanelName);

            // Close Button
            elements.Add(new CuiButton
            {
                Button = { Command = "commands_close", Color = "0.8 0.2 0.2 0.8" },
                RectTransform = { AnchorMin = "0.95 0.92", AnchorMax = "0.99 0.98" },
                Text = { Text = "X", FontSize = 18, Align = TextAnchor.MiddleCenter }
            }, UIPanelName);

            // Vertical Center Divider
            elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.15" },
                RectTransform = { AnchorMin = "0.5 0.03", AnchorMax = "0.502 0.89" }
            }, UIPanelName);

            int totalCommands = config.Commands.Count;
            int itemsPerColumn = (int)Math.Ceiling((double)totalCommands / 2);

            float yStart = 0.89f;
            float blockStep = 0.070f; // Reduced from 0.095f to tighten the gap between commands
            float currentY = yStart;
            int currentIndex = 0;

            foreach (var kvp in config.Commands)
            {
                bool isRightColumn = currentIndex >= itemsPerColumn;

                if (currentIndex == itemsPerColumn)
                {
                    currentY = yStart; 
                }

                string minX = isRightColumn ? "0.52" : "0.03";
                string maxX = isRightColumn ? "0.98" : "0.48";

                // Adjusted bounds to fit within the 0.070f block step
                float cmdYMax = currentY;
                float cmdYMin = currentY - 0.035f; // Increased height to prevent Unity from truncating the text
                float descYMax = cmdYMin; 
                float descYMin = currentY - 0.070f; // Fits perfectly into the new tighter blockStep

                // Command Name
                elements.Add(new CuiLabel
                {
                    // Removed the rich text <color> tag to prevent < > parsing errors.
                    // Used the native Color property instead (RGB converted from #5eb0f9).
                    Text = { Text = kvp.Key, FontSize = 14, Align = TextAnchor.LowerLeft, Color = "0.37 0.69 0.98 1" },
                    RectTransform = { AnchorMin = $"{minX} {cmdYMin}", AnchorMax = $"{maxX} {cmdYMax}" }
                }, UIPanelName);

                // Command Description
                elements.Add(new CuiLabel
                {
                    Text = { Text = kvp.Value, FontSize = 13, Align = TextAnchor.UpperLeft, Color = "0.8 0.8 0.8 1" },
                    RectTransform = { AnchorMin = $"{minX} {descYMin}", AnchorMax = $"{maxX} {descYMax}" }
                }, UIPanelName);

                currentY -= blockStep;
                currentIndex++;
            }

            CuiHelper.AddUi(player, elements);
        }
    }
}