using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("InfoMenu", "Orangemart", "1.1.0")]
    [Description("Replacement for ServerInfo using a compact, high-performance CUI tab system.")]
    public class InfoMenu : CovalencePlugin
    {
        private const string UIPanelName = "InfoMenuUI";
        private const string BlurMat = "assets/content/ui/uibackgroundblur-ingamemenu.mat";

        [Command("info", "welcome")]
        private void CmdShowInfo(IPlayer player, string command, string[] args)
        {
            var rustPlayer = player.Object as BasePlayer;
            if (rustPlayer == null) return;

            // Default to page 1
            DrawInfoUI(rustPlayer, 1);
        }

        [Command("info_switch")]
        private void CmdSwitchInfo(IPlayer player, string command, string[] args)
        {
            var rustPlayer = player.Object as BasePlayer;
            if (rustPlayer == null || args.Length == 0) return;

            if (int.TryParse(args[0], out int page))
                DrawInfoUI(rustPlayer, page);
        }

        [Command("info_close")]
        private void CmdCloseInfo(IPlayer player, string command, string[] args)
        {
            var rustPlayer = player.Object as BasePlayer;
            if (rustPlayer == null) return;
            CuiHelper.DestroyUi(rustPlayer, UIPanelName);
        }

        private void DrawInfoUI(BasePlayer player, int page)
        {
            CuiHelper.DestroyUi(player, UIPanelName);
            var elements = new CuiElementContainer();

            // Shrink the panel significantly: now only takes up the middle 40% of the screen height
            elements.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.95", Material = BlurMat },
                RectTransform = { AnchorMin = "0.25 0.3", AnchorMax = "0.75 0.7" },
                CursorEnabled = true
            }, "Overlay", UIPanelName);

            // Tab Navigation Header (Coordinates are relative to the new, smaller panel)
            CreateTabButton(ref elements, "Welcome", 1, "0.05 0.82", "0.28 0.93", page == 1);
            CreateTabButton(ref elements, "Registration", 2, "0.30 0.82", "0.53 0.93", page == 2);
            CreateTabButton(ref elements, "Compete & Earn", 3, "0.55 0.82", "0.78 0.93", page == 3);

            // Close Button
            elements.Add(new CuiButton
            {
                Button = { Command = "info_close", Color = "0.8 0.2 0.2 0.8" },
                RectTransform = { AnchorMin = "0.88 0.82", AnchorMax = "0.95 0.93" },
                Text = { Text = "X", FontSize = 14, Align = TextAnchor.MiddleCenter }
            }, UIPanelName);

            // Subtle divider line under the tabs
            elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.2" },
                RectTransform = { AnchorMin = "0.05 0.78", AnchorMax = "0.95 0.785" }
            }, UIPanelName);

            // Content Area
            switch (page)
            {
                case 1: DrawWelcomePage(ref elements); break;
                case 2: DrawRegistrationPage(ref elements); break;
                case 3: DrawEarningsPage(ref elements); break;
            }

            CuiHelper.AddUi(player, elements);
        }

        private void CreateTabButton(ref CuiElementContainer elements, string label, int pageNum, string min, string max, bool active)
        {
            string color = active ? "0.37 0.69 0.98 0.8" : "0.3 0.3 0.3 0.5";
            elements.Add(new CuiButton
            {
                Button = { Command = $"info_switch {pageNum}", Color = color },
                RectTransform = { AnchorMin = min, AnchorMax = max },
                Text = { Text = label, FontSize = 13, Align = TextAnchor.MiddleCenter }
            }, UIPanelName);
        }

        private void DrawWelcomePage(ref CuiElementContainer elements)
        {
            elements.Add(new CuiLabel {
                Text = { Text = "Welcome to Orange", FontSize = 26, Align = TextAnchor.MiddleCenter, Color = "1 0.6 0.2 1" },
                RectTransform = { AnchorMin = "0.1 0.55", AnchorMax = "0.9 0.75" }
            }, UIPanelName);

            string body = "- Two Islands: Mandarin (PvE) and Orange (PvP)\n" +
                          "- Earn Bitcoin Prizes\n" +
                          "- Monthly Wipe and Update\n" +
                          "- Procedurally Generated Maps\n" +
                          "- Vanilla Gameplay with Quality of Life Mods";

            elements.Add(new CuiLabel {
                Text = { Text = body, FontSize = 15, Align = TextAnchor.UpperLeft, Color = "0.85 0.85 0.85 1" },
                RectTransform = { AnchorMin = "0.25 0.05", AnchorMax = "0.85 0.55" }
            }, UIPanelName);
        }

        private void DrawRegistrationPage(ref CuiElementContainer elements)
        {
            elements.Add(new CuiLabel {
                Text = { Text = "Join the Competition", FontSize = 26, Align = TextAnchor.MiddleCenter, Color = "1 0.6 0.2 1" },
                RectTransform = { AnchorMin = "0.1 0.55", AnchorMax = "0.9 0.75" }
            }, UIPanelName);

            string body = "Register to compete. Join Discord at dsc.gg/orangemart.\n\n" +
                          "1. Type <color=#a15ef9>/auth</color> in-game to get your code.\n" +
                          "2. DM the code to the Orange Discord Bot.\n" +
                          "3. Start depositing scrap to climb the leaderboard!";

            elements.Add(new CuiLabel {
                Text = { Text = body, FontSize = 15, Align = TextAnchor.UpperLeft, Color = "0.85 0.85 0.85 1" },
                RectTransform = { AnchorMin = "0.20 0.05", AnchorMax = "0.85 0.55" }
            }, UIPanelName);
        }

        private void DrawEarningsPage(ref CuiElementContainer elements)
        {
            elements.Add(new CuiLabel {
                Text = { Text = "Compete to Earn", FontSize = 26, Align = TextAnchor.MiddleCenter, Color = "1 0.6 0.2 1" },
                RectTransform = { AnchorMin = "0.1 0.55", AnchorMax = "0.9 0.75" }
            }, UIPanelName);

            string body = "The Bitcoin prize pool is split based on Proof of Work.\n\n" +
                          "- Deposit scrap at Bloodbanks (Outpost, Bandit, Fishing Village)\n" +
                          "- Type <color=#ffa500>/claim</color> at wipe start for last month's prize\n" +
                          "- Use <color=#ffa500>/sendblood</color> to withdraw to your Lightning wallet";

            elements.Add(new CuiLabel {
                Text = { Text = body, FontSize = 15, Align = TextAnchor.UpperLeft, Color = "0.85 0.85 0.85 1" },
                RectTransform = { AnchorMin = "0.15 0.05", AnchorMax = "0.85 0.55" }
            }, UIPanelName);
        }
    }
}