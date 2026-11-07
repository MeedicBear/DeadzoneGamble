using Steamworks;
using System;

namespace MeedicBear.DeadzoneGamble.Models
{
    public class DuelInvitation
    {
        public CSteamID ChallengerId { get; set; }
        public string ChallengerName { get; set; }
        public CSteamID TargetId { get; set; }
        public string TargetName { get; set; }
        public CurrencyType Currency { get; set; }
        public decimal BetAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public double ExpirationSeconds { get; set; }

        public bool IsExpired => (DateTime.UtcNow - CreatedAt).TotalSeconds >= ExpirationSeconds;
    }
}
