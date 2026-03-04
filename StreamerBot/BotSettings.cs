namespace StreamerBot;

public class BotSettings
{
    public const string SectionName = "BotSettings";

    public int GuestTimeoutMinutes { get; set; } = 30;

    public ulong StreamerRoleId { get; set; }

    public ulong ModRoleId { get; set; }
}
