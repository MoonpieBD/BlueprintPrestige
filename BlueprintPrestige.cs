using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BlueprintPrestige", "Moonpie", "1.0.1")]
    [Description("Prestige system based on blueprints with BetterChat integration and confirmation UI.")]
    public class BlueprintPrestige : RustPlugin
    {
        private Plugin BetterChat;

        private const string PermissionUse = "BlueprintPrestige.use";
        private const string PermissionReset = "BlueprintPrestige.reset";
        private Dictionary<ulong, int> prestigeLevels = new Dictionary<ulong, int>();

        private ConfigData config;

        private class ConfigData
        {
            public Dictionary<string, string> Messages { get; set; } = new()
            {
                ["NoPermission"] = "You do not have permission to use this command.",
                ["ConfirmUsage"] = "Type /prestige yes to confirm your prestige.",
                ["NotAllBlueprints"] = "You haven't learned all blueprints yet.",
                ["BlueprintsReset"] = "All your blueprints have been reset.",
                ["PrestigeUp"] = "Congratulations {player}! You are now Prestige Level {level}.",
                ["PrestigeReset"] = "Prestige level has been reset for player {player}."
            };

            public List<string> IgnoredBlueprints { get; set; } = new()
            {
                "discord.trophy",
                "fogmachine",
                "strobelight",
                "kayak",
                "dart.incapacitate",
                "dart.radiation",
                "dart.scatter",
                "dart.wood",
                "boots.frog",
                "draculacape",
                "m249"
            };


            public string TitleFormat { get; set; } = "[P{level}]";
            public string TitleColorHex { get; set; } = "#FFD700"; // Gold color for title
            public string BroadcastMessage { get; set; } = "{player} has reached Prestige Level {level}!";
        }

        protected override void LoadDefaultConfig() => config = new ConfigData();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<ConfigData>() ?? new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionReset, this);
            LoadData();

            BetterChat = Interface.Oxide.RootPluginManager.GetPlugin("BetterChat");
            if (BetterChat == null)
            {
                PrintWarning("[BlueprintPrestige] Better Chat plugin not found! Titles and chat groups will not work.");
            }
            else
            {
                BetterChat.Call("API_RegisterThirdPartyTitle", this, new Func<IPlayer, string>(GetPrestigeTitle));
            }
        }

        private void Unload() => SaveData();

        private string GetPrestigeTitle(IPlayer iplayer)
        {
            if (ulong.TryParse(iplayer.Id, out ulong userId) && prestigeLevels.TryGetValue(userId, out int level))
            {
                return config.TitleFormat.Replace("{level}", level.ToString());
            }
            return null;
        }

        [ChatCommand("prestige")]
        private void CmdPrestige(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                SendMessage(player, "NoPermission");
                return;
            }

            ShowConfirmationUI(player);
        }

        [ConsoleCommand("prestige.confirm")]
        private void CmdPrestigeConfirm(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                SendMessage(player, "NoPermission");
                return;
            }

            if (!HasAllBlueprints(player))
            {
                SendMessage(player, "NotAllBlueprints");
                DestroyUI(player);
                return;
            }

            ResetBlueprints(player);
            IncreasePrestigeLevel(player);
            DestroyUI(player);
            SaveData();
        }

        [ConsoleCommand("prestige.cancel")]
        private void CmdPrestigeCancel(ConsoleSystem.Arg arg)
        {
            var player = arg.Connection?.player as BasePlayer;
            if (player != null)
                DestroyUI(player);
        }

        [ChatCommand("prestigereset")]
        private void CmdPrestigeReset(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionReset))
            {
                SendMessage(player, "NoPermission");
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage("Usage: /prestigereset <steamID64>");
                return;
            }

            if (!ulong.TryParse(args[0], out ulong targetId))
            {
                player.ChatMessage("Invalid SteamID64.");
                return;
            }

            if (prestigeLevels.ContainsKey(targetId))
            {
                prestigeLevels.Remove(targetId);
                SaveData();

                player.ChatMessage($"Prestige level for player {targetId} has been reset.");

                var targetBasePlayer = BasePlayer.FindByID(targetId);
                if (targetBasePlayer != null)
                    targetBasePlayer.ChatMessage("Your prestige level has been reset by an admin.");
            }
            else
            {
                player.ChatMessage($"Player {targetId} does not have a prestige level set.");
            }
        }

        private bool HasAllBlueprints(BasePlayer player)
        {
            foreach (var bp in ItemManager.bpList)
            {
                if (bp.targetItem == null || bp.defaultBlueprint)
                    continue;

                string shortname = bp.targetItem.shortname;

                if (config.IgnoredBlueprints.Contains(shortname))
                    continue;

                if (!player.blueprints.IsUnlocked(bp.targetItem))
                {
                    PrintWarning($"Player {player.displayName} missing blueprint: {bp.targetItem.displayName.english} ({shortname})");
                    return false;
                }
            }
            return true;
        }

        private void ResetBlueprints(BasePlayer player)
        {
            player.blueprints.Reset();
            SendMessage(player, "BlueprintsReset");
        }

        private void IncreasePrestigeLevel(BasePlayer player)
        {
            ulong userId = player.userID;
            if (!prestigeLevels.ContainsKey(userId))
                prestigeLevels[userId] = 0;

            prestigeLevels[userId]++;
            int level = prestigeLevels[userId];

            SendMessage(player, "PrestigeUp", new Dictionary<string, string>
            {
                ["{player}"] = player.displayName,
                ["{level}"] = level.ToString()
            });

            BroadcastPrestige(player, level);
            string groupName = $"prestige{level}";
            string title = config.TitleFormat.Replace("{level}", level.ToString());

            CreateOrUpdateBetterChatGroup(groupName, title);

            permission.AddUserGroup(player.UserIDString, groupName);
            if (level > 1)
            {
                string previousGroup = $"prestige{level - 1}";
                if (permission.GroupExists(previousGroup))
                {
                    permission.RemoveUserGroup(player.UserIDString, previousGroup);
                }
            }

        }

        private void CreateOrUpdateBetterChatGroup(string groupName, string title)
        {
            if (!permission.GroupExists(groupName))
            {
                permission.CreateGroup(groupName, title, 0);
                BetterChat.Call("API_AddGroup", groupName);
                BetterChat.Call("API_SetGroupField", groupName, "priority", "3");
                BetterChat.Call("API_SetGroupField", groupName, "TitleColor", config.TitleColorHex);
                BetterChat.Call("API_SetGroupField", groupName, "title", title);
            }
        }

        private void AddPlayerToBetterChatGroup(BasePlayer player, string groupName)
        {
            var playerObj = covalence.Players.FindPlayerById(player.UserIDString);
            if (playerObj == null) return;

            permission.AddUserGroup(playerObj.Id, groupName);
        }

        private void SendMessage(BasePlayer player, string key, Dictionary<string, string> tokens = null)
        {
            if (!config.Messages.TryGetValue(key, out string message))
                message = $"[Missing message: {key}]";

            if (tokens != null)
            {
                foreach (var token in tokens)
                    message = message.Replace(token.Key, token.Value);
            }

            player.ChatMessage(message);
        }

        private void BroadcastPrestige(BasePlayer player, int level)
        {
            string msg = config.BroadcastMessage
                .Replace("{player}", player.displayName)
                .Replace("{level}", level.ToString());

            Server.Broadcast(msg);
        }

        private void ShowConfirmationUI(BasePlayer player)
        {
            DestroyUI(player);

            var container = new CuiElementContainer();
            string panelName = "PrestigeUI";

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.95" },
                RectTransform = { AnchorMin = "0.3 0.4", AnchorMax = "0.7 0.6" },
                CursorEnabled = true
            }, "Overlay", panelName);

            container.Add(new CuiLabel
            {
                Text = { Text = "Are you sure you want to prestige?\nThis will reset all your blueprints!", FontSize = 18, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0.1 0.6", AnchorMax = "0.9 0.9" }
            }, panelName);

            container.Add(new CuiButton
            {
                Button = { Command = "prestige.confirm", Color = "0.2 0.8 0.2 1" },
                RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.4 0.4" },
                Text = { Text = "Yes", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, panelName);

            container.Add(new CuiButton
            {
                Button = { Command = "prestige.cancel", Color = "0.8 0.2 0.2 1" },
                RectTransform = { AnchorMin = "0.6 0.1", AnchorMax = "0.9 0.4" },
                Text = { Text = "Cancel", FontSize = 16, Align = TextAnchor.MiddleCenter }
            }, panelName);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "PrestigeUI");
        }

        private void LoadData()
        {
            prestigeLevels = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, int>>(Name) ?? new Dictionary<ulong, int>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, prestigeLevels);
        }


    }
}
