using MeedicBear.DeadzoneGamble.Models;
using Rocket.API;
using UnityEngine;

namespace MeedicBear.DeadzoneGamble
{
    public class DeadzoneGambleConfiguration : IRocketPluginConfiguration
    {
        // UI & Audio Effect IDs
        public ushort UIEffectId { get; set; }
        public ushort ClickAudioEffectId { get; set; }
        public ushort SpinAudioEffectId { get; set; }
        public ushort BangAudioEffectId { get; set; }
        public ushort WinAudioEffectId { get; set; }

        // Cylinder & Rules
        public int CylinderCapacity { get; set; }
        public int MaxSpinsPerPlayer { get; set; }
        public int TurnDurationSeconds { get; set; }
        public int InviteTimeoutSeconds { get; set; }
        public int DuelStartCountdownSeconds { get; set; }
        public bool AutoTriggerOnTimeout { get; set; }
        public float TeleportBackDelaySeconds { get; set; }

        // Economy & Staking
        public CurrencyType DefaultCurrency { get; set; }
        public bool AllowUconomy { get; set; }
        public bool AllowExperience { get; set; }
        public bool AllowItems { get; set; }
        public bool AllowNone { get; set; }
        public decimal MinUconomyBet { get; set; }
        public decimal MaxUconomyBet { get; set; }
        public uint MinExpBet { get; set; }
        public uint MaxExpBet { get; set; }
        public bool DropLoserItemsOnDeath { get; set; }
        public float HouseTaxPercentage { get; set; }

        // Arena & Teleport
        public TeleportMode DefaultTeleportMode { get; set; }
        public bool TeleportBackAfterDuel { get; set; }
        public float MaxDuelDistanceInPlace { get; set; }

        // Arena Coordinates
        public float ArenaPos1X { get; set; }
        public float ArenaPos1Y { get; set; }
        public float ArenaPos1Z { get; set; }
        public float ArenaRot1 { get; set; }

        public float ArenaPos2X { get; set; }
        public float ArenaPos2Y { get; set; }
        public float ArenaPos2Z { get; set; }
        public float ArenaRot2 { get; set; }

        // Safety
        public bool ProtectDuelistsFromOutsideDamage { get; set; }
        public bool FreezeMovementDuringTurn { get; set; }

        public void LoadDefaults()
        {
            UIEffectId = 54800;
            ClickAudioEffectId = 54801;
            SpinAudioEffectId = 54802;
            BangAudioEffectId = 54803;
            WinAudioEffectId = 54804;

            CylinderCapacity = 6;
            MaxSpinsPerPlayer = 1;
            TurnDurationSeconds = 20;
            InviteTimeoutSeconds = 30;
            DuelStartCountdownSeconds = 3;
            AutoTriggerOnTimeout = true;
            TeleportBackDelaySeconds = 20f;

            DefaultCurrency = CurrencyType.Uconomy;
            AllowUconomy = true;
            AllowExperience = true;
            AllowItems = true;
            AllowNone = true;
            MinUconomyBet = 100m;
            MaxUconomyBet = 1000000m;
            MinExpBet = 100;
            MaxExpBet = 50000;
            DropLoserItemsOnDeath = true;
            HouseTaxPercentage = 0f;

            DefaultTeleportMode = TeleportMode.InPlace;
            TeleportBackAfterDuel = true;
            MaxDuelDistanceInPlace = 50f;

            ArenaPos1X = 0f;
            ArenaPos1Y = 0f;
            ArenaPos1Z = 0f;
            ArenaRot1 = 0f;

            ArenaPos2X = 0f;
            ArenaPos2Y = 0f;
            ArenaPos2Z = 0f;
            ArenaRot2 = 0f;

            ProtectDuelistsFromOutsideDamage = true;
            FreezeMovementDuringTurn = true;
        }

        public Vector3 GetArenaPos1() => new Vector3(ArenaPos1X, ArenaPos1Y, ArenaPos1Z);
        public Vector3 GetArenaPos2() => new Vector3(ArenaPos2X, ArenaPos2Y, ArenaPos2Z);

        public bool IsArenaConfigured()
        {
            return ArenaPos1X != 0f || ArenaPos1Y != 0f || ArenaPos1Z != 0f ||
                   ArenaPos2X != 0f || ArenaPos2Y != 0f || ArenaPos2Z != 0f;
        }
    }
}
