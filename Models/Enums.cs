namespace MeedicBear.DeadzoneGamble.Models
{
    public enum CurrencyType
    {
        Uconomy = 0,
        Experience = 1,
        Items = 2,
        None = 3
    }

    public enum TeleportMode
    {
        InPlace = 0,
        Arena = 1
    }

    public enum DuelState
    {
        PendingInvite,
        Countdown,
        TurnActive,
        ResolvingShot,
        Finished
    }

    public enum DuelAction
    {
        PullTrigger,
        SpinCylinder,
        Forfeit
    }
}
