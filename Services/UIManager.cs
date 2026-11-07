using MeedicBear.DeadzoneGamble.Models;
using Rocket.Unturned.Player;
using SDG.NetTransport;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MeedicBear.DeadzoneGamble.Services
{
    public class UIManager
    {
        private readonly DeadzoneGamblePlugin _plugin;
        private readonly DeadzoneGambleConfiguration _config;

        private readonly ConcurrentDictionary<string, string> _avatarCache = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, bool> _fetchingAvatars = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, string> _clientAvatarLoaded = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<CSteamID, EPluginWidgetFlags> _savedWidgetFlags = new ConcurrentDictionary<CSteamID, EPluginWidgetFlags>();

        private const EPluginWidgetFlags MenuHudFlags = EPluginWidgetFlags.ShowLifeMeters |
                                                        EPluginWidgetFlags.ShowStatusIcons |
                                                        EPluginWidgetFlags.ShowUseableGunStatus |
                                                        EPluginWidgetFlags.ShowVehicleStatus |
                                                        EPluginWidgetFlags.ShowCenterDot;

        public const short Key = 5480;

        public event Action<UnturnedPlayer, string> ButtonClicked;

        public UIManager(DeadzoneGamblePlugin plugin)
        {
            _plugin = plugin;
            _config = plugin.Configuration.Instance;
            EffectManager.onEffectButtonClicked += HandleEffectButtonClicked;
        }

        public void Dispose()
        {
            EffectManager.onEffectButtonClicked -= HandleEffectButtonClicked;
            _avatarCache.Clear();
            _fetchingAvatars.Clear();
            _clientAvatarLoaded.Clear();
            _savedWidgetFlags.Clear();
        }

        private void HandleEffectButtonClicked(Player player, string buttonName)
        {
            if (player == null) return;
            var unturnedPlayer = UnturnedPlayer.FromPlayer(player);
            if (unturnedPlayer == null) return;

            // Global Dismiss Button Handler - closes UI immediately
            if (buttonName == "btn_close_summary")
            {
                CloseUI(unturnedPlayer);
                return;
            }

            ButtonClicked?.Invoke(unturnedPlayer, buttonName);
        }

        public void OpenUI(UnturnedPlayer player, DuelSession session)
        {
            if (player == null || session == null || player.Player == null) return;

            try
            {
                // Snapshot existing widget flags
                if (!_savedWidgetFlags.ContainsKey(player.CSteamID))
                {
                    _savedWidgetFlags[player.CSteamID] = player.Player.pluginWidgetFlags & ~(EPluginWidgetFlags.Modal | EPluginWidgetFlags.ForceBlur);
                }

                EPluginWidgetFlags saved = _savedWidgetFlags.TryGetValue(player.CSteamID, out var s) ? s : EPluginWidgetFlags.Default;
                EPluginWidgetFlags openFlags = (saved | EPluginWidgetFlags.Modal | EPluginWidgetFlags.NoBlur) & ~MenuHudFlags;
                player.Player.setAllPluginWidgetFlags(openFlags);

#pragma warning disable CS0618
                EffectManager.sendUIEffect(_config.UIEffectId, Key, player.Player.channel.owner.transportConnection, true);
#pragma warning restore CS0618

                RefreshSessionUI(player, session);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeadzoneGamble] Error opening UI for {player.DisplayName}: {ex.Message}");
            }
        }

        public void CloseUI(UnturnedPlayer player)
        {
            if (player == null || player.Player == null) return;

            try
            {
                EPluginWidgetFlags restore = EPluginWidgetFlags.Default;
                if (_savedWidgetFlags.TryGetValue(player.CSteamID, out var saved) && saved != EPluginWidgetFlags.None)
                {
                    restore = saved;
                }
                _savedWidgetFlags.TryRemove(player.CSteamID, out _);

                // Clear loaded avatar keys for this client
                string prefix = player.CSteamID.ToString() + "_";
                foreach (var k in _clientAvatarLoaded.Keys)
                {
                    if (k.StartsWith(prefix)) _clientAvatarLoaded.TryRemove(k, out _);
                }

                // Guarantee clean non-modal and non-blurred state
                restore &= ~(EPluginWidgetFlags.Modal | EPluginWidgetFlags.ForceBlur);

#pragma warning disable CS0618
                EffectManager.askEffectClearByID(_config.UIEffectId, player.Player.channel.owner.transportConnection);
#pragma warning restore CS0618
                player.Player.setAllPluginWidgetFlags(restore);

                // Double pulse to guarantee mouse re-locks and blur clears
                _plugin.StartCoroutine(ReapplyWidgetFlags(player, restore));
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeadzoneGamble] Error closing UI for {player.DisplayName}: {ex.Message}");
            }
        }

        private IEnumerator ReapplyWidgetFlags(UnturnedPlayer player, EPluginWidgetFlags restore)
        {
            yield return null;
            ForceResendFlags(player, restore);
            yield return new WaitForSeconds(0.25f);
            ForceResendFlags(player, restore);
        }

        private void ForceResendFlags(UnturnedPlayer player, EPluginWidgetFlags restore)
        {
            if (player == null || player.Player == null) return;
            player.Player.setAllPluginWidgetFlags(restore | EPluginWidgetFlags.Modal);
            player.Player.setAllPluginWidgetFlags(restore);
        }

        public void RefreshSessionUI(UnturnedPlayer player, DuelSession session)
        {
            if (player == null || player.Player == null || session == null) return;

            var conn = player.Player.channel.owner.transportConnection;
            if (conn == null) return;

            bool isP1Turn = session.IsPlayer1Turn;
            bool isMyTurn = (isP1Turn && player.CSteamID == session.Player1.CSteamID) ||
                            (!isP1Turn && player.CSteamID == session.Player2.CSteamID);

            // 1. Header Bar
            SendText(conn, "txt_title", "<color=#F5B81F><b>DEADZONE GAMBLE</b></color> <color=#64748B>//</color> <color=#FFFFFF><b>1v1 DUEL</b></color>");
            SendText(conn, "txt_stakes", session.GetStakesDisplayText());

            // 2. Player 1 Card
            SendText(conn, "txt_p1_name", $"<b>{session.Player1.DisplayName}</b>");
            SendText(conn, "txt_p1_status", "<color=#DC3333><b>CHALLENGER</b></color>");
            SendVis(conn, "p1_turn_glow", isP1Turn);
            SendText(conn, "txt_p1_turn_badge", isP1Turn ? "<color=#2EB85C><b>[ YOUR TURN ]</b></color>" : "<color=#64748B>WAITING...</color>");

            ApplyPlayerAvatar(player, conn, session.Player1, "img_p1_avatar", "txt_p1_avatar_fallback", () => RefreshSessionUI(player, session));

            // 3. Player 2 Card
            SendText(conn, "txt_p2_name", $"<b>{session.Player2.DisplayName}</b>");
            SendText(conn, "txt_p2_status", "<color=#26ADE0><b>OPPONENT</b></color>");
            SendVis(conn, "p2_turn_glow", !isP1Turn);
            SendText(conn, "txt_p2_turn_badge", !isP1Turn ? "<color=#2EB85C><b>[ YOUR TURN ]</b></color>" : "<color=#64748B>WAITING...</color>");

            ApplyPlayerAvatar(player, conn, session.Player2, "img_p2_avatar", "txt_p2_avatar_fallback", () => RefreshSessionUI(player, session));

            // 4. Cylinder HUD & Increasing Odds
            int currentPull = session.Cylinder.CurrentPull;
            int displayPull = currentPull + 1;
            if (displayPull > session.Cylinder.Capacity) displayPull = session.Cylinder.Capacity;

            SendText(conn, "txt_cylinder_header", $"<color=#94A3B8>CYLINDER:</color> <color=#FFFFFF><b>PULL {displayPull}</b></color> <color=#64748B>OF</color> <color=#FFFFFF><b>{session.Cylinder.Capacity}</b></color>");
            SendText(conn, "txt_survival_odds", $"<color=#DC3333><b>FATAL ODDS:</b> {session.Cylinder.GetFatalProbability():F1}% (1 IN {session.Cylinder.GetRemainingChambers()})</color>");

            for (int i = 0; i < session.Cylinder.Capacity && i < 6; i++)
            {
                string label;
                if (i < currentPull)
                {
                    label = "<color=#64748B>✕</color>"; // Spent
                }
                else if (i == currentPull)
                {
                    label = "<color=#F5B81F><b>►</b></color>"; // Current trigger
                }
                else
                {
                    label = $"<color=#FFFFFF>{i + 1}</color>"; // Remaining
                }
                SendText(conn, $"txt_ch_{i + 1}", label);
            }

            // 5. Timer
            UnturnedPlayer turnPlayer = isP1Turn ? session.Player1 : session.Player2;
            string timerText = isMyTurn
                ? $"<color=#2EB85C><b>▶ YOUR TURN</b> — {session.TurnTimeRemaining}s REMAINING</color>"
                : $"<color=#F5B81F><b>⏳ {turnPlayer.DisplayName.ToUpper()}'S TURN</b> — {session.TurnTimeRemaining}s</color>";
            SendText(conn, "txt_turn_timer", timerText);

            // 6. Action Feed / Event Log
            SendText(conn, "txt_event_log", session.LastEventText ?? "<color=#94A3B8>Awaiting action...</color>");

            // 7. Interactive Action Buttons
            SendVis(conn, "Panel_Actions", isMyTurn && session.State == DuelState.TurnActive);
            if (isMyTurn)
            {
                int spinsLeft = session.GetSpinsRemaining(player);
                SendText(conn, "txt_btn_pull", "<color=#FFFFFF><b>PULL TRIGGER</b></color>");
                SendText(conn, "txt_btn_spin", $"<color=#FFFFFF><b>SPIN CYLINDER ({spinsLeft})</b></color>");
                SendText(conn, "txt_btn_forfeit", "<color=#CBD5E1><b>FORFEIT</b></color>");
            }
        }

        private void ApplyPlayerAvatar(UnturnedPlayer viewer, ITransportConnection conn, UnturnedPlayer target, string imgChild, string fallbackTextChild, System.Action onFetched)
        {
            if (conn == null || target == null || viewer == null) return;

            string avatarUrl = GetOrFetchAvatar(target, onFetched);
            string clientLoadKey = viewer.CSteamID.ToString() + "_" + imgChild;

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                SendVis(conn, fallbackTextChild, false);
                SendVis(conn, imgChild, true);

                // Anti-flicker: Only send image URL if not already sent to this viewer
                if (!_clientAvatarLoaded.TryGetValue(clientLoadKey, out string sent) || sent != avatarUrl)
                {
                    _clientAvatarLoaded[clientLoadKey] = avatarUrl;
                    SendImgUrl(conn, imgChild, avatarUrl);
                }
            }
            else
            {
                SendVis(conn, imgChild, false);
                SendVis(conn, fallbackTextChild, true);
                string initial = !string.IsNullOrEmpty(target.DisplayName) ? target.DisplayName.Substring(0, 1).ToUpper() : "?";
                SendText(conn, fallbackTextChild, initial);
            }
        }

        public void ShowVictoryOverlay(UnturnedPlayer player, string title, string description, string dismissLabel = null)
        {
            if (player == null || player.Player == null) return;
            var conn = player.Player.channel.owner.transportConnection;
            if (conn == null) return;

            SendVis(conn, "Panel_Actions", false);
            SendVis(conn, "Panel_Winner", true);
            SendText(conn, "txt_winner_title", title);
            SendText(conn, "txt_winner_desc", description);
            SendText(conn, "txt_btn_close", dismissLabel ?? _plugin.Translate("final_screen_dismiss_button"));
        }

        public void PlayClickSound(UnturnedPlayer player)
        {
            PlaySound(player, _config.ClickAudioEffectId, 1);
        }

        public void PlaySpinSound(UnturnedPlayer player)
        {
            PlaySound(player, _config.SpinAudioEffectId, 2);
        }

        public void PlayBangSound(UnturnedPlayer player)
        {
            PlaySound(player, _config.BangAudioEffectId, 3);
        }

        public void PlayWinSound(UnturnedPlayer player)
        {
            PlaySound(player, _config.WinAudioEffectId, 4);
        }

        private void PlaySound(UnturnedPlayer player, ushort effectId, short subKey)
        {
            if (player == null || player.Player == null || effectId == 0) return;
            try
            {
#pragma warning disable CS0618
                EffectManager.sendUIEffect(effectId, (short)(Key + 10 + subKey), player.Player.channel.owner.transportConnection, true);
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeadzoneGamble] Error playing sound effect {effectId}: {ex.Message}");
            }
        }

        private void SendText(ITransportConnection conn, string childName, string text)
        {
            try
            {
                EffectManager.sendUIEffectText(Key, conn, true, childName, text ?? "");
            }
            catch { }
        }

        private void SendVis(ITransportConnection conn, string childName, bool visible)
        {
            try
            {
                EffectManager.sendUIEffectVisibility(Key, conn, true, childName, visible);
            }
            catch { }
        }

        private void SendImgUrl(ITransportConnection conn, string childName, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                EffectManager.sendUIEffectImageURL(Key, conn, true, childName, url, true, true);
            }
            catch { }
        }

        public string GetOrFetchAvatar(UnturnedPlayer player, System.Action onFetched)
        {
            if (player == null) return null;
            string sid = player.CSteamID.ToString();

            if (_avatarCache.TryGetValue(sid, out string cachedUrl))
            {
                return cachedUrl;
            }

            try
            {
                var profile = player.SteamProfile;
                string url = profile?.AvatarFull?.ToString() ?? profile?.AvatarMedium?.ToString() ?? profile?.AvatarIcon?.ToString();
                if (!string.IsNullOrEmpty(url))
                {
                    _avatarCache[sid] = url;
                    return url;
                }
            }
            catch { }

            if (_fetchingAvatars.TryAdd(sid, true))
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (var wc = new WebClient())
                        {
                            wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                            string xml = wc.DownloadString($"https://steamcommunity.com/profiles/{sid}/?xml=1");
                            if (!string.IsNullOrEmpty(xml))
                            {
                                int idx = xml.IndexOf("<avatarFull><![CDATA[", StringComparison.OrdinalIgnoreCase);
                                if (idx != -1)
                                {
                                    int start = idx + "<avatarFull><![CDATA[".Length;
                                    int end = xml.IndexOf("]]></avatarFull>", start, StringComparison.OrdinalIgnoreCase);
                                    if (end != -1)
                                    {
                                        string avatar = xml.Substring(start, end - start).Trim();
                                        if (!string.IsNullOrEmpty(avatar))
                                        {
                                            _avatarCache[sid] = avatar;
                                            onFetched?.Invoke();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        _fetchingAvatars.TryRemove(sid, out bool _);
                    }
                });
            }

            return null;
        }
    }
}
