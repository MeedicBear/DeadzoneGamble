using MeedicBear.DeadzoneGamble.Models;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MeedicBear.DeadzoneGamble.Services
{
    public class EconomyService
    {
        private readonly DeadzoneGambleConfiguration _config;
        private object _uconomyDatabaseInstance;
        private MethodInfo _getBalanceMethod;
        private MethodInfo _increaseBalanceMethod;
        private bool _uconomyResolved;

        private readonly ConcurrentDictionary<string, (decimal Balance, DateTime FetchedAt)> _balanceCache =
            new ConcurrentDictionary<string, (decimal Balance, DateTime FetchedAt)>();

        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(5);

        public EconomyService(DeadzoneGambleConfiguration config)
        {
            _config = config;
            Task.Run(ResolveUconomyReflection);
        }

        private void ResolveUconomyReflection()
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var uconType = asm.GetType("fr34kyn01535.Uconomy.Uconomy");
                    if (uconType != null)
                    {
                        var instField = uconType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                        var uconInst = instField?.GetValue(null);
                        if (uconInst != null)
                        {
                            var dbField = uconType.GetField("Database", BindingFlags.Public | BindingFlags.Instance);
                            _uconomyDatabaseInstance = dbField?.GetValue(uconInst);
                            if (_uconomyDatabaseInstance != null)
                            {
                                var dbType = _uconomyDatabaseInstance.GetType();
                                _getBalanceMethod = dbType.GetMethod("GetBalance", new[] { typeof(string) });
                                _increaseBalanceMethod = dbType.GetMethod("IncreaseBalance", new[] { typeof(string), typeof(decimal) });
                                _uconomyResolved = _getBalanceMethod != null && _increaseBalanceMethod != null;
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[DeadzoneGamble] Uconomy reflection lookup failed: {ex.Message}");
                _uconomyResolved = false;
            }
        }

        public async Task<decimal> GetBalanceAsync(string steamId, UnturnedPlayer player, CurrencyType currency)
        {
            if (string.IsNullOrEmpty(steamId)) return 0m;

            if (currency == CurrencyType.Uconomy)
            {
                if (_balanceCache.TryGetValue(steamId, out var cached) && (DateTime.UtcNow - cached.FetchedAt) < CacheTtl)
                {
                    return cached.Balance;
                }

                return await Task.Run(() =>
                {
                    if (!_uconomyResolved) ResolveUconomyReflection();
                    if (_uconomyResolved && _uconomyDatabaseInstance != null && _getBalanceMethod != null)
                    {
                        try
                        {
                            object res = _getBalanceMethod.Invoke(_uconomyDatabaseInstance, new object[] { steamId });
                            decimal val = Convert.ToDecimal(res);
                            _balanceCache[steamId] = (val, DateTime.UtcNow);
                            return val;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"[DeadzoneGamble] Async Uconomy GetBalance error for {steamId}: {ex.Message}");
                        }
                    }
                    return 0m;
                }).ConfigureAwait(false);
            }

            if (currency == CurrencyType.Experience && player != null)
            {
                return player.Experience;
            }

            return 0m;
        }

        public async Task<bool> CheckFundsAsync(UnturnedPlayer player, CurrencyType currency, decimal amount)
        {
            if (player == null) return false;
            if (currency == CurrencyType.None) return true;

            if (currency == CurrencyType.Items)
            {
                return player.Inventory != null;
            }

            if (currency == CurrencyType.Experience)
            {
                return player.Experience >= (uint)Math.Max(0, Math.Round(amount));
            }

            if (currency == CurrencyType.Uconomy)
            {
                decimal bal = await GetBalanceAsync(player.CSteamID.ToString(), player, currency).ConfigureAwait(false);
                return bal >= amount;
            }

            return false;
        }

        public async Task<bool> DeductBetAsync(UnturnedPlayer player, CurrencyType currency, decimal amount)
        {
            if (player == null) return false;
            if (currency == CurrencyType.None || currency == CurrencyType.Items || amount <= 0m) return true;

            if (currency == CurrencyType.Experience)
            {
                uint xp = (uint)Math.Max(0, Math.Round(amount));
                if (player.Experience < xp) return false;
                player.Experience -= xp;
                return true;
            }

            if (currency == CurrencyType.Uconomy)
            {
                string steamId = player.CSteamID.ToString();
                return await Task.Run(() =>
                {
                    if (!_uconomyResolved) ResolveUconomyReflection();
                    if (_uconomyResolved && _uconomyDatabaseInstance != null && _getBalanceMethod != null && _increaseBalanceMethod != null)
                    {
                        try
                        {
                            object res = _getBalanceMethod.Invoke(_uconomyDatabaseInstance, new object[] { steamId });
                            decimal curBal = Convert.ToDecimal(res);
                            if (curBal < amount) return false;

                            _increaseBalanceMethod.Invoke(_uconomyDatabaseInstance, new object[] { steamId, -amount });
                            _balanceCache[steamId] = (curBal - amount, DateTime.UtcNow);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"[DeadzoneGamble] Async Uconomy deduct error: {ex.Message}");
                        }
                    }
                    return false;
                }).ConfigureAwait(false);
            }

            return false;
        }

        public async Task PayoutWinnerAsync(UnturnedPlayer winner, UnturnedPlayer loser, CurrencyType currency, decimal betAmount, float taxPercent)
        {
            if (winner == null) return;

            decimal totalPot = betAmount * 2m;
            decimal tax = totalPot * (decimal)(Mathf.Clamp(taxPercent, 0f, 100f) / 100f);
            decimal netPayout = totalPot - tax;

            if (currency == CurrencyType.Experience)
            {
                uint xpWon = (uint)Math.Max(0, Math.Round(netPayout));
                winner.Experience += xpWon;
            }
            else if (currency == CurrencyType.Uconomy)
            {
                string winnerId = winner.CSteamID.ToString();
                await Task.Run(() =>
                {
                    if (!_uconomyResolved) ResolveUconomyReflection();
                    if (_uconomyResolved && _uconomyDatabaseInstance != null && _increaseBalanceMethod != null)
                    {
                        try
                        {
                            _increaseBalanceMethod.Invoke(_uconomyDatabaseInstance, new object[] { winnerId, netPayout });
                            if (_balanceCache.TryGetValue(winnerId, out var cur))
                            {
                                _balanceCache[winnerId] = (cur.Balance + netPayout, DateTime.UtcNow);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"[DeadzoneGamble] Async Uconomy deposit error: {ex.Message}");
                        }
                    }
                }).ConfigureAwait(false);
            }
            else if (currency == CurrencyType.Items && _config.DropLoserItemsOnDeath && loser != null)
            {
                DropPlayerItemsAtFeet(loser.Player);
            }
        }

        public async Task RefundBetAsync(UnturnedPlayer player, CurrencyType currency, decimal amount)
        {
            if (player == null || currency == CurrencyType.None || currency == CurrencyType.Items || amount <= 0m) return;

            if (currency == CurrencyType.Experience)
            {
                player.Experience += (uint)Math.Max(0, Math.Round(amount));
            }
            else if (currency == CurrencyType.Uconomy)
            {
                string steamId = player.CSteamID.ToString();
                await Task.Run(() =>
                {
                    if (!_uconomyResolved) ResolveUconomyReflection();
                    if (_uconomyResolved && _uconomyDatabaseInstance != null && _increaseBalanceMethod != null)
                    {
                        try
                        {
                            _increaseBalanceMethod.Invoke(_uconomyDatabaseInstance, new object[] { steamId, amount });
                            if (_balanceCache.TryGetValue(steamId, out var cur))
                            {
                                _balanceCache[steamId] = (cur.Balance + amount, DateTime.UtcNow);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError($"[DeadzoneGamble] Async Uconomy refund error: {ex.Message}");
                        }
                    }
                }).ConfigureAwait(false);
            }
        }

        public void DropPlayerItemsAtFeet(Player player)
        {
            if (player == null || player.inventory == null) return;

            try
            {
                Vector3 dropPos = player.transform.position + Vector3.up * 0.5f;

                for (byte page = 0; page < PlayerInventory.PAGES; page++)
                {
                    if (player.inventory.items[page] == null) continue;
                    byte count = player.inventory.items[page].getItemCount();
                    for (int i = count - 1; i >= 0; i--)
                    {
                        var jar = player.inventory.items[page].getItem((byte)i);
                        if (jar != null && jar.item != null)
                        {
                            ItemManager.dropItem(jar.item, dropPos, false, true, true);
                            player.inventory.removeItem(page, (byte)i);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeadzoneGamble] Error dropping player items at feet: {ex.Message}");
            }
        }
    }
}
