using System;

namespace MeedicBear.DeadzoneGamble.Models
{
    public class PlayerDuelStats
    {
        public string SteamID { get; set; }
        public string PlayerName { get; set; }
        public int DuelsPlayed { get; set; }
        public int DuelsWon { get; set; }
        public int DuelsLost { get; set; }
        public int CurrentStreak { get; set; }
        public int BestStreak { get; set; }
        public decimal TotalUconomyWon { get; set; }
        public uint TotalExpWon { get; set; }
        public DateTime LastPlayed { get; set; }

        public float WinRate => DuelsPlayed > 0 ? ((float)DuelsWon / DuelsPlayed) * 100f : 0f;
    }
}
