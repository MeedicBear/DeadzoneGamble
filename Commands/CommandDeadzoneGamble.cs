using MeedicBear.DeadzoneGamble.Models;
using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeedicBear.DeadzoneGamble.Commands
{
    public class CommandDeadzoneGamble : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "deadzone";
        public string Help => "1v1 Russian Roulette Deadzone Gamble duel";
        public string Syntax => "<player|accept|decline|cancel|stats|setpos1|setpos2> [amount] [uconomy|exp|items|none]";
        public List<string> Aliases => new List<string> { "dz", "roulette", "duel" };
        public List<string> Permissions => new List<string> { "deadzone.gamble" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            var plugin = DeadzoneGamblePlugin.Instance;
            var config = plugin.Configuration.Instance;
            var duelManager = plugin.DuelManager;

            if (command.Length == 0)
            {
                UnturnedChat.Say(player, "--- [ DEADZONE GAMBLE 1v1 ] ---", Color.yellow);
                UnturnedChat.Say(player, "/dz <player> [bet] [uconomy|exp|items|none] - Challenge player", Color.cyan);
                UnturnedChat.Say(player, "/dz accept - Accept incoming duel challenge", Color.cyan);
                UnturnedChat.Say(player, "/dz decline - Decline challenge", Color.cyan);
                UnturnedChat.Say(player, "/dz cancel - Cancel outgoing challenge", Color.cyan);
                UnturnedChat.Say(player, "/dz stats - View your duel statistics", Color.cyan);
                if (player.HasPermission("deadzone.admin"))
                {
                    UnturnedChat.Say(player, "[Admin] /dz setpos1 | /dz setpos2 | /dz setmode <inplace|arena>", Color.magenta);
                }
                return;
            }

            string sub = command[0].ToLowerInvariant();

            // 1. Accept
            if (sub == "accept" || sub == "a")
            {
                duelManager.AcceptChallenge(player);
                return;
            }

            // 2. Decline
            if (sub == "decline" || sub == "d" || sub == "deny")
            {
                duelManager.DeclineChallenge(player);
                return;
            }

            // 3. Cancel
            if (sub == "cancel" || sub == "c")
            {
                duelManager.CancelChallenge(player);
                return;
            }

            // 4. Stats
            if (sub == "stats")
            {
                var stats = duelManager.GetOrAddStats(player.CSteamID.ToString(), player.DisplayName);
                UnturnedChat.Say(player, $"--- [{player.DisplayName}'s Duel Stats] ---", Color.yellow);
                UnturnedChat.Say(player, $"Duels: {stats.DuelsPlayed} | Won: {stats.DuelsWon} | Lost: {stats.DuelsLost} (Winrate: {stats.WinRate:F1}%)", Color.cyan);
                UnturnedChat.Say(player, $"Current Streak: {stats.CurrentStreak} | Best Streak: {stats.BestStreak}", Color.cyan);
                UnturnedChat.Say(player, $"Total Won: ${stats.TotalUconomyWon:N0} Uconomy | {stats.TotalExpWon:N0} EXP", Color.green);
                return;
            }

            // 5. Admin: setpos1
            if (sub == "setpos1" || sub == "setposa")
            {
                if (!player.HasPermission("deadzone.admin"))
                {
                    UnturnedChat.Say(player, plugin.Translate("no_permission"), Color.red);
                    return;
                }

                config.ArenaPos1X = player.Position.x;
                config.ArenaPos1Y = player.Position.y;
                config.ArenaPos1Z = player.Position.z;
                config.ArenaRot1 = player.Rotation;
                plugin.Configuration.Save();

                UnturnedChat.Say(player, $"Arena Spawn 1 set to: ({config.ArenaPos1X:F1}, {config.ArenaPos1Y:F1}, {config.ArenaPos1Z:F1}) Rot: {config.ArenaRot1:F1}", Color.green);
                return;
            }

            // 6. Admin: setpos2
            if (sub == "setpos2" || sub == "setposb")
            {
                if (!player.HasPermission("deadzone.admin"))
                {
                    UnturnedChat.Say(player, plugin.Translate("no_permission"), Color.red);
                    return;
                }

                config.ArenaPos2X = player.Position.x;
                config.ArenaPos2Y = player.Position.y;
                config.ArenaPos2Z = player.Position.z;
                config.ArenaRot2 = player.Rotation;
                plugin.Configuration.Save();

                UnturnedChat.Say(player, $"Arena Spawn 2 set to: ({config.ArenaPos2X:F1}, {config.ArenaPos2Y:F1}, {config.ArenaPos2Z:F1}) Rot: {config.ArenaRot2:F1}", Color.green);
                return;
            }

            // 7. Admin: setmode
            if (sub == "setmode")
            {
                if (!player.HasPermission("deadzone.admin"))
                {
                    UnturnedChat.Say(player, plugin.Translate("no_permission"), Color.red);
                    return;
                }

                if (command.Length < 2)
                {
                    UnturnedChat.Say(player, "Usage: /dz setmode <inplace|arena>", Color.yellow);
                    return;
                }

                string modeStr = command[1].ToLowerInvariant();
                if (modeStr == "arena")
                {
                    config.DefaultTeleportMode = TeleportMode.Arena;
                    plugin.Configuration.Save();
                    UnturnedChat.Say(player, "Duel mode set to ARENA teleportation.", Color.green);
                }
                else if (modeStr == "inplace" || modeStr == "place")
                {
                    config.DefaultTeleportMode = TeleportMode.InPlace;
                    plugin.Configuration.Save();
                    UnturnedChat.Say(player, "Duel mode set to IN-PLACE duels.", Color.green);
                }
                else
                {
                    UnturnedChat.Say(player, "Invalid mode. Use 'inplace' or 'arena'.", Color.red);
                }
                return;
            }

            // 8. Challenge Player: /dz <player> [amount] [currency]
            UnturnedPlayer target = UnturnedPlayer.FromName(command[0]);
            if (target == null)
            {
                UnturnedChat.Say(player, plugin.Translate("player_not_found", command[0]), Color.red);
                return;
            }

            decimal amount = 0m;
            CurrencyType currency = config.DefaultCurrency;

            if (command.Length >= 2)
            {
                string arg2 = command[1].ToLowerInvariant();
                if (arg2 == "items" || arg2 == "item")
                {
                    currency = CurrencyType.Items;
                }
                else if (arg2 == "none" || arg2 == "honor")
                {
                    currency = CurrencyType.None;
                }
                else if (decimal.TryParse(command[1], out decimal parsedAmount))
                {
                    amount = parsedAmount;
                }
            }

            if (command.Length >= 3)
            {
                string curStr = command[2].ToLowerInvariant();
                if (curStr == "uconomy" || curStr == "money" || curStr == "$")
                {
                    currency = CurrencyType.Uconomy;
                }
                else if (curStr == "exp" || curStr == "xp")
                {
                    currency = CurrencyType.Experience;
                }
                else if (curStr == "items" || curStr == "item")
                {
                    currency = CurrencyType.Items;
                }
                else if (curStr == "none" || curStr == "honor")
                {
                    currency = CurrencyType.None;
                }
            }

            duelManager.SendChallenge(player, target, currency, amount);
        }
    }
}
