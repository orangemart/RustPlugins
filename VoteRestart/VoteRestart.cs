using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Vote Restart", "Orangemart", "1.0.0")]
    [Description("Allows players to vote for a server restart with configurable thresholds and cooldowns.")]
    public class VoteRestart : RustPlugin
    {
        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Required Yes Percentage (0.0 to 1.0)")]
            public float RequiredPercentage = 0.80f; // Default 80%

            [JsonProperty(PropertyName = "Vote Duration (Seconds)")]
            public float VoteDuration = 60f;

            [JsonProperty(PropertyName = "Cooldown Between Votes (Seconds)")]
            public float CooldownDuration = 300f; // Default 5 minutes

            [JsonProperty(PropertyName = "Minimum Players Online to Vote")]
            public int MinimumPlayers = 2;

            [JsonProperty(PropertyName = "Restart Countdown Timer (Seconds)")]
            public int RestartTimer = 10;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion

        #region State Variables

        private bool isVoteActive = false;
        private DateTime lastVoteTime = DateTime.MinValue;
        private Timer voteTimer;
        
        private HashSet<ulong> yesVotes = new HashSet<ulong>();
        private HashSet<ulong> noVotes = new HashSet<ulong>();

        #endregion

        #region Commands

        [ChatCommand("voterestart")]
        private void CmdVoteRestart(BasePlayer player, string command, string[] args)
        {
            if (isVoteActive)
            {
                SendReply(player, "<color=#ff4d4d>A vote is already in progress!</color> Type <color=#ffa64d>/vote yes</color> or <color=#ffa64d>/vote no</color>.");
                return;
            }

            int onlinePlayers = BasePlayer.activePlayerList.Count;
            if (onlinePlayers < config.MinimumPlayers)
            {
                SendReply(player, $"<color=#ff4d4d>Not enough players online to start a vote. ({onlinePlayers}/{config.MinimumPlayers})</color>");
                return;
            }

            double secondsSinceLastVote = (DateTime.UtcNow - lastVoteTime).TotalSeconds;
            if (secondsSinceLastVote < config.CooldownDuration)
            {
                int remainingCooldown = (int)(config.CooldownDuration - secondsSinceLastVote);
                SendReply(player, $"<color=#ff4d4d>You must wait {remainingCooldown} seconds before starting another vote.</color>");
                return;
            }

            StartVote(player);
        }

        [ChatCommand("vote")]
        private void CmdVote(BasePlayer player, string command, string[] args)
        {
            if (!isVoteActive)
            {
                SendReply(player, "<color=#ff4d4d>There is no active restart vote.</color>");
                return;
            }

            if (args.Length != 1)
            {
                SendReply(player, "Syntax: <color=#ffa64d>/vote yes</color> or <color=#ffa64d>/vote no</color>");
                return;
            }

            string voteCast = args[0].ToLower();

            if (yesVotes.Contains(player.userID) || noVotes.Contains(player.userID))
            {
                SendReply(player, "<color=#ff4d4d>You have already cast your vote!</color>");
                return;
            }

            if (voteCast == "yes" || voteCast == "y")
            {
                yesVotes.Add(player.userID);
                SendReply(player, "<color=#85e085>You voted YES for a server restart.</color>");
                CheckVoteStatus();
            }
            else if (voteCast == "no" || voteCast == "n")
            {
                noVotes.Add(player.userID);
                SendReply(player, "<color=#ff4d4d>You voted NO for a server restart.</color>");
                CheckVoteStatus();
            }
            else
            {
                SendReply(player, "Invalid vote. Use <color=#ffa64d>/vote yes</color> or <color=#ffa64d>/vote no</color>.");
            }
        }

        #endregion

        #region Core Logic

        private void StartVote(BasePlayer initiator)
        {
            isVoteActive = true;
            lastVoteTime = DateTime.UtcNow;
            yesVotes.Clear();
            noVotes.Clear();

            // Auto-vote yes for the person who started it
            yesVotes.Add(initiator.userID);

            float thresholdPercent = config.RequiredPercentage * 100;
            Server.Broadcast($"<color=#ffa64d>{initiator.displayName} has started a vote to RESTART the server!</color>\n<color=#cccccc>We need {thresholdPercent}% of players to vote yes.</color>\nType <color=#85e085>/vote yes</color> or <color=#ff4d4d>/vote no</color>. Vote ends in {config.VoteDuration} seconds.");

            voteTimer = timer.Once(config.VoteDuration, EndVote);
            
            // Check immediately in case the threshold is met right at the start (e.g., 2 players online)
            CheckVoteStatus();
        }

        private void CheckVoteStatus()
        {
            if (!isVoteActive) return;

            int totalPlayers = BasePlayer.activePlayerList.Count;
            int requiredYes = Mathf.CeilToInt(totalPlayers * config.RequiredPercentage);

            if (yesVotes.Count >= requiredYes)
            {
                TriggerRestart(yesVotes.Count, totalPlayers);
            }
            else if ((yesVotes.Count + noVotes.Count) == totalPlayers)
            {
                // Everyone voted, but threshold wasn't met
                EndVote();
            }
        }

        private void EndVote()
        {
            if (!isVoteActive) return;
            isVoteActive = false;

            if (voteTimer != null && !voteTimer.Destroyed)
            {
                voteTimer.Destroy();
            }

            int totalPlayers = BasePlayer.activePlayerList.Count;
            int requiredYes = Mathf.CeilToInt(totalPlayers * config.RequiredPercentage);

            if (yesVotes.Count >= requiredYes)
            {
                TriggerRestart(yesVotes.Count, totalPlayers);
            }
            else
            {
                Server.Broadcast($"<color=#ff4d4d>Vote Restart FAILED.</color> ({yesVotes.Count} Yes / {requiredYes} required).");
            }
        }

        private void TriggerRestart(int yesCount, int totalPlayers)
        {
            isVoteActive = false;
            
            if (voteTimer != null && !voteTimer.Destroyed)
            {
                voteTimer.Destroy();
            }

            Server.Broadcast($"<color=#85e085>Vote Restart PASSED!</color> ({yesCount}/{totalPlayers} voted Yes). Server restarting in {config.RestartTimer} seconds!");
            
            // Native Rust server restart command triggers a graceful shutdown and save
            rust.RunServerCommand($"restart {config.RestartTimer} \"Player Vote Restart Passed\"");
        }

        #endregion
    }
}