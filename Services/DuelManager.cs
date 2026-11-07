using MeedicBear.DeadzoneGamble.Models;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using Steamworks;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using Logger = Rocket.Core.Logging.Logger;

namespace MeedicBear.DeadzoneGamble.Services
{
    public class DuelManager
    {
        private readonly DeadzoneGamblePlugin _plugin;
        private readonly DeadzoneGambleConfiguration _config;
        private readonly EconomyService _economy;
        private readonly UIManager _ui;

        private readonly Dictionary<CSteamID, DuelInvitation> _pendingInvitations = new Dictionary<CSteamID, DuelInvitation>();
        private readonly List<DuelSession> _activeSessions = new List<DuelSession>();
        private readonly ConcurrentDictionary<string, PlayerDuelStats> _playerStats = new ConcurrentDictionary<string, PlayerDuelStats>();

        private string StatsFilePath => Path.Combine(_plugin.Directory, "DeadzoneGamble_Stats.json");

        public DuelManager(DeadzoneGamblePlugin plugin)
        {
            _plugin = plugin;
            _config = plugin.Configuration.Instance;
            _economy = plugin.Economy;
            _ui = plugin.UI;

            LoadStats();
            _ui.ButtonClicked += HandleGlobalButtonClicked;
        }

        public void Dispose()
        {
            _ui.ButtonClicked -= HandleGlobalButtonClicked;
            SaveStats();

            for (int i = _activeSessions.Count - 1; i >= 0; i--)
            {
                _activeSessions[i].AbortDuel("Plugin reload / shutdown");
            }
            _activeSessions.Clear();
            _pendingInvitations.Clear();
        }

        private void HandleGlobalButtonClicked(UnturnedPlayer player, string buttonName)
        {
            if (player == null) return;

            if (buttonName == "btn_close_summary")
            {
                _ui.CloseUI(player);
                return;
            }

            DuelSession session = GetActiveSession(player.CSteamID);
            if (session != null)
            {
                session.HandleButtonClick(player, buttonName);
            }
        }

        public async void SendChallenge(UnturnedPlayer challenger, UnturnedPlayer target, CurrencyType currency, decimal betAmount)
        {
            if (challenger == null || target == null) return;

            if (challenger.CSteamID == target.CSteamID)
            {
                UnturnedChat.Say(challenger, _plugin.Translate("cannot_challenge_self"), Color.red);
                return;
            }

            if (IsInActiveDuel(challenger.CSteamID))
            {
                UnturnedChat.Say(challenger, _plugin.Translate("already_in_duel"), Color.red);
                return;
            }

            if (IsInActiveDuel(target.CSteamID))
            {
                UnturnedChat.Say(challenger, _plugin.Translate("target_already_in_duel", target.DisplayName), Color.red);
                return;
            }

            // Check distance if InPlace
            if (_config.DefaultTeleportMode == TeleportMode.InPlace)
            {
                float dist = Vector3.Distance(challenger.Position, target.Position);
                if (dist > _config.MaxDuelDistanceInPlace)
                {
                    UnturnedChat.Say(challenger, _plugin.Translate("too_far_away", (int)_config.MaxDuelDistanceInPlace), Color.red);
                    return;
                }
            }

            // Validate Bet Limits
            if (currency == CurrencyType.Uconomy)
            {
                if (betAmount < _config.MinUconomyBet || betAmount > _config.MaxUconomyBet)
                {
                    UnturnedChat.Say(challenger, _plugin.Translate("invalid_uconomy_bet", _config.MinUconomyBet, _config.MaxUconomyBet), Color.red);
                    return;
                }
            }
            else if (currency == CurrencyType.Experience)
            {
                if (betAmount < _config.MinExpBet || betAmount > _config.MaxExpBet)
                {
                    UnturnedChat.Say(challenger, _plugin.Translate("invalid_exp_bet", _config.MinExpBet, _config.MaxExpBet), Color.red);
                    return;
                }
            }

            // Check Challenger Funds
            bool hasFunds = await _economy.CheckFundsAsync(challenger, currency, betAmount);
            if (!hasFunds)
            {
                UnturnedChat.Say(challenger, _plugin.Translate("challenger_insufficient_funds"), Color.red);
                return;
            }

            // Register Invitation
            var invite = new DuelInvitation
            {
                ChallengerId = challenger.CSteamID,
                ChallengerName = challenger.DisplayName,
                TargetId = target.CSteamID,
                TargetName = target.DisplayName,
                Currency = currency,
                BetAmount = betAmount,
                CreatedAt = DateTime.UtcNow,
                ExpirationSeconds = _config.InviteTimeoutSeconds
            };

            _pendingInvitations[target.CSteamID] = invite;

            string betDesc = GetBetDescription(currency, betAmount);
            UnturnedChat.Say(challenger, _plugin.Translate("challenge_sent", target.DisplayName, betDesc), Color.green);
            UnturnedChat.Say(target, _plugin.Translate("challenge_received", challenger.DisplayName, betDesc), Color.yellow);
            UnturnedChat.Say(target, _plugin.Translate("challenge_prompt"), Color.cyan);
        }

        public async void AcceptChallenge(UnturnedPlayer target)
        {
            if (target == null) return;

            if (!_pendingInvitations.TryGetValue(target.CSteamID, out var invite) || invite.IsExpired)
            {
                UnturnedChat.Say(target, _plugin.Translate("no_pending_challenge"), Color.red);
                _pendingInvitations.Remove(target.CSteamID);
                return;
            }

            UnturnedPlayer challenger = UnturnedPlayer.FromCSteamID(invite.ChallengerId);
            if (challenger == null)
            {
                UnturnedChat.Say(target, _plugin.Translate("challenger_offline"), Color.red);
                _pendingInvitations.Remove(target.CSteamID);
                return;
            }

            if (IsInActiveDuel(challenger.CSteamID))
            {
                UnturnedChat.Say(target, _plugin.Translate("challenger_in_duel"), Color.red);
                _pendingInvitations.Remove(target.CSteamID);
                return;
            }

            // Check Funds for target
            bool targetHasFunds = await _economy.CheckFundsAsync(target, invite.Currency, invite.BetAmount);
            if (!targetHasFunds)
            {
                UnturnedChat.Say(target, _plugin.Translate("target_insufficient_funds"), Color.red);
                return;
            }

            // Check Funds for challenger again
            bool challengerHasFunds = await _economy.CheckFundsAsync(challenger, invite.Currency, invite.BetAmount);
            if (!challengerHasFunds)
            {
                UnturnedChat.Say(target, _plugin.Translate("challenger_lost_funds"), Color.red);
                _pendingInvitations.Remove(target.CSteamID);
                return;
            }

            // Deduct bets
            bool d1 = await _economy.DeductBetAsync(challenger, invite.Currency, invite.BetAmount);
            bool d2 = await _economy.DeductBetAsync(target, invite.Currency, invite.BetAmount);

            if (!d1 || !d2)
            {
                // Refund if partial failure
                if (d1) await _economy.RefundBetAsync(challenger, invite.Currency, invite.BetAmount);
                if (d2) await _economy.RefundBetAsync(target, invite.Currency, invite.BetAmount);

                UnturnedChat.Say(target, _plugin.Translate("bet_deduction_failed"), Color.red);
                _pendingInvitations.Remove(target.CSteamID);
                return;
            }

            _pendingInvitations.Remove(target.CSteamID);

            // Start Duel Session
            var session = new DuelSession(_plugin, challenger, target, invite.Currency, invite.BetAmount);
            session.DuelFinished += OnSessionFinished;
            _activeSessions.Add(session);
            session.StartDuel();
        }

        public void DeclineChallenge(UnturnedPlayer target)
        {
            if (target == null) return;
            if (_pendingInvitations.TryGetValue(target.CSteamID, out var invite))
            {
                UnturnedPlayer challenger = UnturnedPlayer.FromCSteamID(invite.ChallengerId);
                if (challenger != null)
                {
                    UnturnedChat.Say(challenger, _plugin.Translate("challenge_declined", target.DisplayName), Color.red);
                }
                _pendingInvitations.Remove(target.CSteamID);
                UnturnedChat.Say(target, _plugin.Translate("challenge_declined_self"), Color.yellow);
            }
            else
            {
                UnturnedChat.Say(target, _plugin.Translate("no_pending_challenge"), Color.red);
            }
        }

        public void CancelChallenge(UnturnedPlayer challenger)
        {
            if (challenger == null) return;

            CSteamID foundTarget = CSteamID.Nil;
            foreach (var kvp in _pendingInvitations)
            {
                if (kvp.Value.ChallengerId == challenger.CSteamID)
                {
                    foundTarget = kvp.Key;
                    break;
                }
            }

            if (foundTarget != CSteamID.Nil)
            {
                _pendingInvitations.Remove(foundTarget);
                UnturnedChat.Say(challenger, _plugin.Translate("challenge_cancelled"), Color.yellow);
            }
            else
            {
                UnturnedChat.Say(challenger, _plugin.Translate("no_outgoing_challenge"), Color.red);
            }
        }

        private void OnSessionFinished(DuelSession session, UnturnedPlayer winner, UnturnedPlayer loser)
        {
            session.DuelFinished -= OnSessionFinished;
            _activeSessions.Remove(session);

            // Record stats
            if (winner != null)
            {
                var wStats = GetOrAddStats(winner.CSteamID.ToString(), winner.DisplayName);
                wStats.DuelsPlayed++;
                wStats.DuelsWon++;
                wStats.CurrentStreak++;
                if (wStats.CurrentStreak > wStats.BestStreak) wStats.BestStreak = wStats.CurrentStreak;
                if (session.Currency == CurrencyType.Uconomy) wStats.TotalUconomyWon += session.BetAmount * 2m;
                if (session.Currency == CurrencyType.Experience) wStats.TotalExpWon += (uint)(session.BetAmount * 2m);
                wStats.LastPlayed = DateTime.UtcNow;
            }

            if (loser != null)
            {
                var lStats = GetOrAddStats(loser.CSteamID.ToString(), loser.DisplayName);
                lStats.DuelsPlayed++;
                lStats.DuelsLost++;
                lStats.CurrentStreak = 0;
                lStats.LastPlayed = DateTime.UtcNow;
            }

            SaveStats();
        }

        public void HandlePlayerDisconnected(UnturnedPlayer player)
        {
            if (player == null) return;

            // Remove any pending invite
            _pendingInvitations.Remove(player.CSteamID);

            // Forfeit in active duel if any
            DuelSession session = GetActiveSession(player.CSteamID);
            if (session != null && session.State == DuelState.TurnActive)
            {
                session.ExecuteForfeit(player);
            }
        }

        public bool IsInActiveDuel(CSteamID steamId)
        {
            return GetActiveSession(steamId) != null;
        }

        public DuelSession GetActiveSession(CSteamID steamId)
        {
            for (int i = 0; i < _activeSessions.Count; i++)
            {
                if (_activeSessions[i].ContainsPlayer(steamId))
                {
                    return _activeSessions[i];
                }
            }
            return null;
        }

        public PlayerDuelStats GetOrAddStats(string steamId, string name)
        {
            return _playerStats.GetOrAdd(steamId, (id) => new PlayerDuelStats
            {
                SteamID = id,
                PlayerName = name,
                LastPlayed = DateTime.UtcNow
            });
        }

        public string GetBetDescription(CurrencyType currency, decimal amount)
        {
            switch (currency)
            {
                case CurrencyType.Uconomy: return $"${amount:N0} Uconomy";
                case CurrencyType.Experience: return $"{amount:N0} EXP";
                case CurrencyType.Items: return "All Carried Items";
                case CurrencyType.None:
                default: return "Honor & Survival";
            }
        }

        private void LoadStats()
        {
            try
            {
                if (File.Exists(StatsFilePath))
                {
                    string json = File.ReadAllText(StatsFilePath);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, PlayerDuelStats>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict) _playerStats[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeadzoneGamble] Error loading duel stats: {ex.Message}");
            }
        }

        private void SaveStats()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_playerStats, Formatting.Indented);
                File.WriteAllText(StatsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeadzoneGamble] Error saving duel stats: {ex.Message}");
            }
        }
    }
}
