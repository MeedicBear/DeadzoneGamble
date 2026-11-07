using MeedicBear.DeadzoneGamble.Services;
using Rocket.API.Collections;
using Rocket.Core.Plugins;
using Rocket.Unturned;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using Logger = Rocket.Core.Logging.Logger;

namespace MeedicBear.DeadzoneGamble
{
    public class DeadzoneGamblePlugin : RocketPlugin<DeadzoneGambleConfiguration>
    {
        public static DeadzoneGamblePlugin Instance { get; private set; }

        public EconomyService Economy { get; private set; }
        public UIManager UI { get; private set; }
        public DuelManager DuelManager { get; private set; }

        protected override void Load()
        {
            Instance = this;

            Economy = new EconomyService(Configuration.Instance);
            UI = new UIManager(this);
            DuelManager = new DuelManager(this);

            U.Events.OnPlayerDisconnected += OnPlayerDisconnected;
            DamageTool.damagePlayerRequested += OnDamagePlayerRequested;

            Logger.Log("===============================================================================");
            Logger.Log("  ____                  _                      ____                 _     _      ");
            Logger.Log(" |  _ \\  ___  __ _  __| |_______  _ __   ___  / ___| __ _ _ __ ___ | |__ | | ___ ");
            Logger.Log(" | | | |/ _ \\/ _` |/ _` |_  / _ \\| '_ \\ / _ \\| |  _ / _` | '_ ` _ \\| '_ \\| |/ _ \\");
            Logger.Log(" | |_| |  __/ (_| | (_| |/ / (_) | | | |  __/| |_| | (_| | | | | | | |_) | |  __/");
            Logger.Log(" |____/ \\___|\\__,_|\\__,_/___\\___/|_| |_|\\___| \\____|\\__,_|_| |_| |_|_.__/|_|\\___|");
            Logger.Log("                                                                             ");
            Logger.Log("                       DEADZONE GAMBLE // 1v1 DUEL                           ");
            Logger.Log("                     Proudly Hosted on UnturnedForge.com                     ");
            Logger.Log("                    Get More Plugins: https://unturnedforge.com               ");
            Logger.Log("===============================================================================");
            Logger.Log($"[DeadzoneGamble] Version: {Assembly.GetName().Version} | UI Effect ID: {Configuration.Instance.UIEffectId}");
            Logger.Log($"[DeadzoneGamble] Teleport Mode: {Configuration.Instance.DefaultTeleportMode} | Cylinder: {Configuration.Instance.CylinderCapacity} Chambers");
            Logger.Log("===============================================================================");
        }

        protected override void Unload()
        {
            U.Events.OnPlayerDisconnected -= OnPlayerDisconnected;
            DamageTool.damagePlayerRequested -= OnDamagePlayerRequested;

            DuelManager?.Dispose();
            UI?.Dispose();

            Economy = null;
            UI = null;
            DuelManager = null;
            Instance = null;

            Logger.Log("===============================================================================");
            Logger.Log("[DeadzoneGamble] Plugin unloaded. Thank you for using UnturnedForge.com!");
            Logger.Log("===============================================================================");
        }

        private void OnPlayerDisconnected(UnturnedPlayer player)
        {
            if (player == null) return;
            DuelManager?.HandlePlayerDisconnected(player);
        }

        private void OnDamagePlayerRequested(ref DamagePlayerParameters parameters, ref bool shouldAllow)
        {
            if (!Configuration.Instance.ProtectDuelistsFromOutsideDamage || parameters.player == null) return;

            CSteamID victimId = parameters.player.channel.owner.playerID.steamID;
            if (DuelManager == null || !DuelManager.IsInActiveDuel(victimId)) return;

            // Allow the fatal duel shot from self
            if (parameters.killer == victimId && parameters.damage >= 100)
            {
                return;
            }

            // Block external damage (zombies, outside snipers, griefers)
            shouldAllow = false;
        }

        public override TranslationList DefaultTranslations => new TranslationList
        {
            { "no_permission", "You do not have permission to use this command." },
            { "player_not_found", "Player '{0}' was not found online." },
            { "cannot_challenge_self", "You cannot challenge yourself to a Russian Roulette duel!" },
            { "already_in_duel", "You are already participating in an active duel!" },
            { "target_already_in_duel", "{0} is already in a duel or has a pending challenge." },
            { "too_far_away", "You must be within {0} meters of the target to challenge in-place!" },
            { "invalid_uconomy_bet", "Uconomy bet must be between ${0:N0} and ${1:N0}." },
            { "invalid_exp_bet", "Experience bet must be between {0:N0} and {1:N0} EXP." },
            { "challenger_insufficient_funds", "You do not have enough funds/items for this bet!" },
            { "target_insufficient_funds", "You do not have enough funds/items to accept this duel!" },
            { "challenger_lost_funds", "The challenger no longer has sufficient funds for this duel." },
            { "bet_deduction_failed", "Failed to secure duel stakes. Duel cancelled." },
            { "challenge_sent", "You challenged {0} to a Deadzone Gamble duel for [{1}]!" },
            { "challenge_received", "{0} has challenged you to a Deadzone Gamble 1v1 duel for [{1}]!" },
            { "challenge_prompt", "Type /dz accept to accept or /dz decline to decline (expires in 30s)." },
            { "challenge_declined", "{0} declined your duel challenge." },
            { "challenge_declined_self", "You declined the duel challenge." },
            { "challenge_cancelled", "You cancelled your outgoing duel challenge." },
            { "no_outgoing_challenge", "You do not have an outgoing challenge to cancel." },
            { "no_pending_challenge", "You do not have any pending duel challenges." },
            { "challenger_offline", "The challenger has gone offline." },
            { "challenger_in_duel", "The challenger entered another duel." },
            { "duel_started", "The Deadzone Gamble duel against {0} has begun! Take your positions." },
            { "not_your_turn", "It is not your turn to act!" },
            { "no_spins_remaining", "You have no revolver cylinder spins remaining!" },
            { "duel_won_global", "[Deadzone Gamble] {0} defeated {1} in a 1v1 Russian Roulette duel and claimed [{2}]!" },
            { "duel_aborted", "The duel was aborted: {0}" },
            { "teleport_back_notice", "You will be teleported back to your pre-duel location in {0} seconds. Loot your opponent now!" },
            { "final_screen_victory_title", "<color=#F5B81F><b>DEAD MAN STANDING!</b></color>" },
            { "final_screen_victory_desc", "<color=#FFFFFF>{0}</color> <color=#DC3333>was eliminated!</color> <color=#FFFFFF>{1}</color> <color=#2EB85C>survives and claims</color> <color=#F5B81F><b>{2}</b></color>!" },
            { "final_screen_forfeit_title", "<color=#F5B81F><b>VICTORY BY FORFEIT!</b></color>" },
            { "final_screen_forfeit_desc", "<color=#FFFFFF>{0}</color> <color=#64748B>surrendered!</color> <color=#FFFFFF>{1}</color> <color=#2EB85C>wins</color> <color=#F5B81F><b>{2}</b></color>!" },
            { "final_screen_eliminated_title", "<color=#DC3333><b>ELIMINATED</b></color>" },
            { "final_screen_eliminated_desc", "<color=#FFFFFF>{0}</color> <color=#2EB85C>survived the duel and claimed</color> <color=#F5B81F><b>{1}</b></color>." },
            { "final_screen_dismiss_button", "DISMISS" }
        };
    }
}
