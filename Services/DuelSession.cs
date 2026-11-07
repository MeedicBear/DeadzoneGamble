using MeedicBear.DeadzoneGamble.Models;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MeedicBear.DeadzoneGamble.Services
{
    public class DuelSession
    {
        private readonly DeadzoneGamblePlugin _plugin;
        private readonly DeadzoneGambleConfiguration _config;
        private readonly EconomyService _economy;
        private readonly UIManager _ui;

        public UnturnedPlayer Player1 { get; }
        public UnturnedPlayer Player2 { get; }
        public CurrencyType Currency { get; }
        public decimal BetAmount { get; }
        public DuelCylinder Cylinder { get; }

        public DuelState State { get; private set; }
        public bool IsPlayer1Turn { get; private set; }
        public int TurnTimeRemaining { get; private set; }
        public string LastEventText { get; private set; }

        public Vector3 PreDuelPosP1 { get; private set; }
        public float PreDuelRotP1 { get; private set; }
        public Vector3 PreDuelPosP2 { get; private set; }
        public float PreDuelRotP2 { get; private set; }

        public bool IsTeleportArenaUsed { get; private set; }

        private readonly Dictionary<CSteamID, int> _spinsUsed = new Dictionary<CSteamID, int>();
        private Coroutine _turnTimerCoroutine;
        private Coroutine _cleanupCoroutine;

        public event Action<DuelSession, UnturnedPlayer, UnturnedPlayer> DuelFinished;

        public DuelSession(
            DeadzoneGamblePlugin plugin,
            UnturnedPlayer p1,
            UnturnedPlayer p2,
            CurrencyType currency,
            decimal betAmount)
        {
            _plugin = plugin;
            _config = plugin.Configuration.Instance;
            _economy = plugin.Economy;
            _ui = plugin.UI;

            Player1 = p1;
            Player2 = p2;
            Currency = currency;
            BetAmount = betAmount;

            Cylinder = new DuelCylinder(_config.CylinderCapacity);
            _spinsUsed[Player1.CSteamID] = 0;
            _spinsUsed[Player2.CSteamID] = 0;

            State = DuelState.Countdown;
            IsPlayer1Turn = true; // Challenger has first turn
            TurnTimeRemaining = _config.TurnDurationSeconds;
            LastEventText = $"<color=#94A3B8>Duel initiated between</color> <color=#FFFFFF><b>{Player1.DisplayName}</b></color> <color=#64748B>and</color> <color=#FFFFFF><b>{Player2.DisplayName}</b></color>!";
        }

        public void StartDuel()
        {
            // Cache pre-duel positions
            PreDuelPosP1 = Player1.Position;
            PreDuelRotP1 = Player1.Rotation;
            PreDuelPosP2 = Player2.Position;
            PreDuelRotP2 = Player2.Rotation;

            // Handle Arena Teleport if enabled
            if (_config.DefaultTeleportMode == TeleportMode.Arena && _config.IsArenaConfigured())
            {
                IsTeleportArenaUsed = true;
                Player1.Teleport(_config.GetArenaPos1(), _config.ArenaRot1);
                Player2.Teleport(_config.GetArenaPos2(), _config.ArenaRot2);
            }
            else
            {
                IsTeleportArenaUsed = false;
                // Face each other in-place
                Vector3 dirToP2 = (Player2.Position - Player1.Position).normalized;
                if (dirToP2 != Vector3.zero)
                {
                    float angleToP2 = Quaternion.LookRotation(dirToP2).eulerAngles.y;
                    float angleToP1 = Quaternion.LookRotation(-dirToP2).eulerAngles.y;
                    Player1.Teleport(Player1.Position, angleToP2);
                    Player2.Teleport(Player2.Position, angleToP1);
                }
            }

            // Freeze player movements
            if (_config.FreezeMovementDuringTurn)
            {
                FreezePlayer(Player1, true);
                FreezePlayer(Player2, true);
            }

            // Open UIs
            _ui.OpenUI(Player1, this);
            _ui.OpenUI(Player2, this);

            // Announce in chat
            UnturnedChat.Say(Player1, _plugin.Translate("duel_started", Player2.DisplayName), Color.yellow);
            UnturnedChat.Say(Player2, _plugin.Translate("duel_started", Player1.DisplayName), Color.yellow);

            State = DuelState.TurnActive;
            _turnTimerCoroutine = _plugin.StartCoroutine(TurnTimerLoop());
        }

        public void HandleButtonClick(UnturnedPlayer player, string buttonName)
        {
            if (buttonName == "btn_close_summary")
            {
                _ui.CloseUI(player);
                return;
            }

            if (State != DuelState.TurnActive) return;
            if (player.CSteamID != Player1.CSteamID && player.CSteamID != Player2.CSteamID) return;

            bool isP1 = (player.CSteamID == Player1.CSteamID);
            if (isP1 != IsPlayer1Turn)
            {
                UnturnedChat.Say(player, _plugin.Translate("not_your_turn"), Color.red);
                return;
            }

            switch (buttonName)
            {
                case "btn_pull_trigger":
                    ExecutePullTrigger(player);
                    break;

                case "btn_spin_cylinder":
                    ExecuteSpinCylinder(player);
                    break;

                case "btn_forfeit":
                    ExecuteForfeit(player);
                    break;
            }
        }

        public void ExecutePullTrigger(UnturnedPlayer player)
        {
            if (State != DuelState.TurnActive) return;

            bool isP1 = (player.CSteamID == Player1.CSteamID);
            UnturnedPlayer opponent = isP1 ? Player2 : Player1;

            bool isBang = Cylinder.PullTrigger();

            if (isBang)
            {
                // Fatal shot!
                State = DuelState.ResolvingShot;
                if (_turnTimerCoroutine != null)
                {
                    _plugin.StopCoroutine(_turnTimerCoroutine);
                    _turnTimerCoroutine = null;
                }

                LastEventText = $"<color=#DC3333><b>*BANG!*</b></color> <color=#FFFFFF>{player.DisplayName}</color> <color=#DC3333>pulled the trigger and took a lethal shot!</color>";
                _ui.PlayBangSound(Player1);
                _ui.PlayBangSound(Player2);

                _ui.RefreshSessionUI(Player1, this);
                _ui.RefreshSessionUI(Player2, this);

                // Eliminate player
                KillPlayer(player);

                // End match with opponent as winner
                _plugin.StartCoroutine(EndMatchRoutine(opponent, player, wasForfeit: false));
            }
            else
            {
                // Blank / Survived!
                LastEventText = $"<color=#94A3B8>*</color><color=#F5B81F><b>CLICK!</b></color><color=#94A3B8>*</color> <color=#FFFFFF>{player.DisplayName}</color> <color=#2EB85C>survived chamber {Cylinder.CurrentPull}!</color> <color=#64748B>Passing revolver...</color>";
                _ui.PlayClickSound(Player1);
                _ui.PlayClickSound(Player2);

                // Switch turn
                IsPlayer1Turn = !IsPlayer1Turn;
                TurnTimeRemaining = _config.TurnDurationSeconds;

                _ui.RefreshSessionUI(Player1, this);
                _ui.RefreshSessionUI(Player2, this);
            }
        }

        public void ExecuteSpinCylinder(UnturnedPlayer player)
        {
            if (State != DuelState.TurnActive) return;

            int used = _spinsUsed[player.CSteamID];
            if (used >= _config.MaxSpinsPerPlayer)
            {
                UnturnedChat.Say(player, _plugin.Translate("no_spins_remaining"), Color.red);
                return;
            }

            _spinsUsed[player.CSteamID] = used + 1;
            Cylinder.Spin();

            _ui.PlaySpinSound(Player1);
            _ui.PlaySpinSound(Player2);

            LastEventText = $"<color=#F5B81F><b>↻ SPIN!</b></color> <color=#FFFFFF>{player.DisplayName}</color> <color=#94A3B8>spun the revolver cylinder!</color>";
            _ui.RefreshSessionUI(Player1, this);
            _ui.RefreshSessionUI(Player2, this);
        }

        public void ExecuteForfeit(UnturnedPlayer player)
        {
            if (State != DuelState.TurnActive) return;

            State = DuelState.ResolvingShot;
            if (_turnTimerCoroutine != null)
            {
                _plugin.StopCoroutine(_turnTimerCoroutine);
                _turnTimerCoroutine = null;
            }

            bool isP1 = (player.CSteamID == Player1.CSteamID);
            UnturnedPlayer opponent = isP1 ? Player2 : Player1;

            LastEventText = $"<color=#DC3333><b>SURRENDER!</b></color> <color=#FFFFFF>{player.DisplayName}</color> <color=#64748B>conceded the duel!</color>";
            _plugin.StartCoroutine(EndMatchRoutine(opponent, player, wasForfeit: true));
        }

        private IEnumerator TurnTimerLoop()
        {
            while (State == DuelState.TurnActive)
            {
                yield return new WaitForSeconds(1f);
                if (State != DuelState.TurnActive) yield break;

                TurnTimeRemaining--;

                if (TurnTimeRemaining <= 0)
                {
                    UnturnedPlayer currentTurnPlayer = IsPlayer1Turn ? Player1 : Player2;

                    if (_config.AutoTriggerOnTimeout)
                    {
                        LastEventText = $"<color=#DC3333><b>TIME EXPIRED!</b></color> <color=#FFFFFF>{currentTurnPlayer.DisplayName}'s</color> <color=#64748B>finger was forced on the trigger!</color>";
                        _ui.RefreshSessionUI(Player1, this);
                        _ui.RefreshSessionUI(Player2, this);
                        ExecutePullTrigger(currentTurnPlayer);
                    }
                    else
                    {
                        LastEventText = $"<color=#DC3333><b>TIME EXPIRED!</b></color> <color=#FFFFFF>{currentTurnPlayer.DisplayName}</color> <color=#64748B>forfeited the match!</color>";
                        _ui.RefreshSessionUI(Player1, this);
                        _ui.RefreshSessionUI(Player2, this);
                        ExecuteForfeit(currentTurnPlayer);
                    }
                    yield break;
                }

                _ui.RefreshSessionUI(Player1, this);
                _ui.RefreshSessionUI(Player2, this);
            }
        }

        private IEnumerator EndMatchRoutine(UnturnedPlayer winner, UnturnedPlayer loser, bool wasForfeit)
        {
            yield return new WaitForSeconds(1.2f);
            State = DuelState.Finished;

            _ui.PlayWinSound(winner);

            string winTitle = wasForfeit
                ? _plugin.Translate("final_screen_forfeit_title")
                : _plugin.Translate("final_screen_victory_title");

            string winDesc = wasForfeit
                ? _plugin.Translate("final_screen_forfeit_desc", loser.DisplayName, winner.DisplayName, GetPlainStakesText())
                : _plugin.Translate("final_screen_victory_desc", loser.DisplayName, winner.DisplayName, GetPlainStakesText());

            string loseTitle = wasForfeit
                ? _plugin.Translate("final_screen_forfeit_title")
                : _plugin.Translate("final_screen_eliminated_title");

            string loseDesc = wasForfeit
                ? _plugin.Translate("final_screen_forfeit_desc", loser.DisplayName, winner.DisplayName, GetPlainStakesText())
                : _plugin.Translate("final_screen_eliminated_desc", winner.DisplayName, GetPlainStakesText());

            string dismissLabel = _plugin.Translate("final_screen_dismiss_button");

            _ui.ShowVictoryOverlay(winner, winTitle, winDesc, dismissLabel);
            if (loser != null && loser.Player != null)
            {
                _ui.ShowVictoryOverlay(loser, loseTitle, loseDesc, dismissLabel);
            }

            // Award pot to winner on worker pool
            _plugin.StartCoroutine(PayoutRoutine(winner, loser));

            // Notify server
            UnturnedChat.Say(_plugin.Translate("duel_won_global", winner.DisplayName, loser.DisplayName, GetPlainStakesText()), Color.green);

            DuelFinished?.Invoke(this, winner, loser);

            // Teleport return coroutine
            if (IsTeleportArenaUsed && _config.TeleportBackAfterDuel)
            {
                _cleanupCoroutine = _plugin.StartCoroutine(TeleportBackRoutine(winner));
            }
            else
            {
                UnfreezePlayer(winner);
                UnfreezePlayer(loser);
            }
        }

        private IEnumerator PayoutRoutine(UnturnedPlayer winner, UnturnedPlayer loser)
        {
            var task = _economy.PayoutWinnerAsync(winner, loser, Currency, BetAmount, _config.HouseTaxPercentage);
            while (!task.IsCompleted)
            {
                yield return null;
            }
        }

        private IEnumerator TeleportBackRoutine(UnturnedPlayer winner)
        {
            float delay = Mathf.Max(3f, _config.TeleportBackDelaySeconds);
            UnturnedChat.Say(winner, _plugin.Translate("teleport_back_notice", (int)delay), Color.cyan);

            yield return new WaitForSeconds(delay);

            if (winner != null && winner.Player != null)
            {
                Vector3 returnPos = (winner.CSteamID == Player1.CSteamID) ? PreDuelPosP1 : PreDuelPosP2;
                float returnRot = (winner.CSteamID == Player1.CSteamID) ? PreDuelRotP1 : PreDuelRotP2;

                winner.Teleport(returnPos, returnRot);
                UnfreezePlayer(winner);
                _ui.CloseUI(winner);
            }
        }

        private void KillPlayer(UnturnedPlayer player)
        {
            if (player == null || player.Player == null) return;
            try
            {
                player.Player.life.askDamage(101, Vector3.up, EDeathCause.GUN, ELimb.SKULL, CSteamID.Nil, out _);

                if (_config.DropLoserItemsOnDeath)
                {
                    _economy.DropPlayerItemsAtFeet(player.Player);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeadzoneGamble] Error dealing lethal duel damage to {player.DisplayName}: {ex.Message}");
            }
        }

        public void AbortDuel(string reason)
        {
            if (State == DuelState.Finished) return;
            State = DuelState.Finished;

            if (_turnTimerCoroutine != null) _plugin.StopCoroutine(_turnTimerCoroutine);
            if (_cleanupCoroutine != null) _plugin.StopCoroutine(_cleanupCoroutine);

            UnfreezePlayer(Player1);
            UnfreezePlayer(Player2);

            _ui.CloseUI(Player1);
            _ui.CloseUI(Player2);

            _plugin.StartCoroutine(RefundRoutine(Player1));
            _plugin.StartCoroutine(RefundRoutine(Player2));

            if (Player1 != null) UnturnedChat.Say(Player1, _plugin.Translate("duel_aborted", reason), Color.red);
            if (Player2 != null) UnturnedChat.Say(Player2, _plugin.Translate("duel_aborted", reason), Color.red);
        }

        private IEnumerator RefundRoutine(UnturnedPlayer player)
        {
            if (player == null) yield break;
            var task = _economy.RefundBetAsync(player, Currency, BetAmount);
            while (!task.IsCompleted) yield return null;
        }

        public int GetSpinsRemaining(UnturnedPlayer player)
        {
            if (player == null) return 0;
            int used = _spinsUsed.TryGetValue(player.CSteamID, out int count) ? count : 0;
            return Mathf.Max(0, _config.MaxSpinsPerPlayer - used);
        }

        public string GetStakesDisplayText()
        {
            switch (Currency)
            {
                case CurrencyType.Uconomy:
                    return $"<color=#94A3B8>STAKES:</color> <color=#2EB85C><b>${BetAmount:N0} UCONOMY</b></color>  <color=#64748B>|</color>  <color=#94A3B8>POT:</color> <color=#F5B81F><b>${(BetAmount * 2m):N0}</b></color>";
                case CurrencyType.Experience:
                    return $"<color=#94A3B8>STAKES:</color> <color=#26ADE0><b>{BetAmount:N0} EXP</b></color>  <color=#64748B>|</color>  <color=#94A3B8>POT:</color> <color=#F5B81F><b>{(BetAmount * 2m):N0}</b></color>";
                case CurrencyType.Items:
                    return "<color=#DC3333><b>ALL CARRIED ITEMS (DEATH DROP)</b></color>";
                case CurrencyType.None:
                default:
                    return "<color=#94A3B8>HONOR & SURVIVAL</color>";
            }
        }

        public string GetPlainStakesText()
        {
            switch (Currency)
            {
                case CurrencyType.Uconomy: return $"${BetAmount * 2m:N0} Uconomy";
                case CurrencyType.Experience: return $"{BetAmount * 2m:N0} EXP";
                case CurrencyType.Items: return "all dropped items";
                case CurrencyType.None:
                default: return "Honor & Glory";
            }
        }

        private void FreezePlayer(UnturnedPlayer player, bool freeze)
        {
            if (player == null || player.Player == null) return;
            try
            {
                player.Player.movement.sendPluginSpeedMultiplier(freeze ? 0f : 1f);
                player.Player.movement.sendPluginJumpMultiplier(freeze ? 0f : 1f);
            }
            catch { }
        }

        private void UnfreezePlayer(UnturnedPlayer player)
        {
            FreezePlayer(player, false);
        }

        public bool ContainsPlayer(CSteamID steamId)
        {
            return (Player1 != null && Player1.CSteamID == steamId) ||
                   (Player2 != null && Player2.CSteamID == steamId);
        }
    }
}
