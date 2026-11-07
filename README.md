# Deadzone Gamble — 1v1 Russian Roulette Duel Plugin for Unturned (RocketMod)

**Deadzone Gamble** is a standalone, immersive 1v1 Russian Roulette duel plugin for Unturned servers featuring a dedicated custom Unturned Glazier UI, realistic revolver cylinder mechanics, dynamic survival odds, customizable betting currencies (Uconomy, Experience, All Items on Death, or pure Honor), and tactical arena/in-place teleportation.

---

## Features

* **High-Stakes 1v1 Duels**:
  * Challenge any player: `/dz <player> [bet] [uconomy|exp|items|none]`
  * Accept or decline with `/dz accept` or `/dz decline` (auto-expires in 30 seconds).
  * Safe staking with automatic refund if the match is aborted.

* **Multiple Staking Modes**:
  * **Uconomy**: Bet server money with configurable minimum and maximum limits.
  * **Experience (EXP)**: Bet Unturned player experience.
  * **Items (Death Drop)**: When a player receives the lethal chamber, all their carried clothing and inventory items drop at their feet for the winner to loot.
  * **None**: Pure survival and pride.

* **Arena Teleportation & In-Place Options**:
  * **In-Place**: Players face each other directly on the spot, locked in place during the duel.
  * **Arena Teleport**: Pre-configure two arena duel positions (`/dz setpos1` and `/dz setpos2`). Players are teleported to the arena and, once the match concludes, the survivor is teleported back to their original position after a configurable delay (e.g. 20s) so they can loot the body!

* **Dynamic Unturned Glazier UI**:
  * Steam avatar rendering for both challenger and opponent via `EffectManager.sendUIEffectImageURL`.
  * Dynamic odds meter calculating exact survival / fatal probabilities (1 in 6, 1 in 5, 1 in 4, etc.).
  * 6 chamber status indicators tracking visited/empty vs current chambers.
  * Turn countdown timer with automatic trigger pull on expiration.
  * Interactive action buttons: **PULL TRIGGER**, **SPIN CYLINDER**, and **FORFEIT**.
  * Complete custom synthesized audio: dry-fire hammer click, cylinder ratchet spin, gunshot bang, and victory sting.

* **Anti-Interference & Protection**:
  * Duelists are shielded with godmode against external snipers, griefers, and zombies for the duration of the duel.
  * Movement speed/jump is frozen during the turn to ensure fair play.

---

## Commands & Permissions

| Command | Description | Default Permission |
|---|---|---|
| `/dz <player> [bet] [type]` | Challenge a player to a 1v1 Russian Roulette duel | `deadzone.gamble` |
| `/dz accept` | Accept an incoming duel challenge | `deadzone.gamble` |
| `/dz decline` | Decline an incoming duel challenge | `deadzone.gamble` |
| `/dz cancel` | Cancel your outgoing duel challenge | `deadzone.gamble` |
| `/dz stats` | View your duel win/loss record, streaks, and earnings | `deadzone.gamble` |
| `/dz setpos1` | (Admin) Set Arena Position 1 to your current coordinates | `deadzone.admin` |
| `/dz setpos2` | (Admin) Set Arena Position 2 to your current coordinates | `deadzone.admin` |
| `/dz setmode <inplace\|arena>` | (Admin) Switch between in-place dueling and arena teleportation | `deadzone.admin` |

*Aliases*: `/roulette`, `/duel`

---

## Configuration (`DeadzoneGamble.configuration.xml`)

```xml
<?xml version="1.0" encoding="utf-8"?>
<DeadzoneGambleConfiguration xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <!-- Effect IDs -->
  <UIEffectId>54800</UIEffectId>
  <ClickAudioEffectId>54801</ClickAudioEffectId>
  <SpinAudioEffectId>54802</SpinAudioEffectId>
  <BangAudioEffectId>54803</BangAudioEffectId>
  <WinAudioEffectId>54804</WinAudioEffectId>

  <!-- Rules & Revolver -->
  <CylinderCapacity>6</CylinderCapacity>
  <MaxSpinsPerPlayer>1</MaxSpinsPerPlayer>
  <TurnDurationSeconds>20</TurnDurationSeconds>
  <InviteTimeoutSeconds>30</InviteTimeoutSeconds>
  <DuelStartCountdownSeconds>3</DuelStartCountdownSeconds>
  <AutoTriggerOnTimeout>true</AutoTriggerOnTimeout>
  <TeleportBackDelaySeconds>20</TeleportBackDelaySeconds>

  <!-- Currency & Economy -->
  <DefaultCurrency>Uconomy</DefaultCurrency>
  <AllowUconomy>true</AllowUconomy>
  <AllowExperience>true</AllowExperience>
  <AllowItems>true</AllowItems>
  <AllowNone>true</AllowNone>
  <MinUconomyBet>100</MinUconomyBet>
  <MaxUconomyBet>1000000</MaxUconomyBet>
  <MinExpBet>100</MinExpBet>
  <MaxExpBet>50000</MaxExpBet>
  <DropLoserItemsOnDeath>true</DropLoserItemsOnDeath>
  <HouseTaxPercentage>0</HouseTaxPercentage>

  <!-- Arena & Teleportation -->
  <DefaultTeleportMode>InPlace</DefaultTeleportMode>
  <TeleportBackAfterDuel>true</TeleportBackAfterDuel>
  <MaxDuelDistanceInPlace>50</MaxDuelDistanceInPlace>

  <ArenaPos1X>0</ArenaPos1X>
  <ArenaPos1Y>0</ArenaPos1Y>
  <ArenaPos1Z>0</ArenaPos1Z>
  <ArenaRot1>0</ArenaRot1>

  <ArenaPos2X>0</ArenaPos2X>
  <ArenaPos2Y>0</ArenaPos2Y>
  <ArenaPos2Z>0</ArenaPos2Z>
  <ArenaRot2>0</ArenaRot2>

  <!-- Safety -->
  <ProtectDuelistsFromOutsideDamage>true</ProtectDuelistsFromOutsideDamage>
  <FreezeMovementDuringTurn>true</FreezeMovementDuringTurn>
</DeadzoneGambleConfiguration>
```

---

## Installation

1. Copy `DeadzoneGamble.dll` from `bin/Release/net48/` into your server's `Rocket/Plugins/` directory.
2. Copy the entire `DeadzoneGambleUI` folder into your server's `Sandbox/Bundles/` or server workshop folder.
3. Restart the server or run `rocket reload DeadzoneGamble`.

---

## Disclaimer

> [!NOTE]
> This plugin and its accompanying assets were developed with the assistance of artificial intelligence (AI) tools, in collaboration with human developers for system design, architecture, rigorous testing, and code review.

