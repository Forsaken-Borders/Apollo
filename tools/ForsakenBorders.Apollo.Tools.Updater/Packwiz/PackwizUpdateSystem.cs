namespace ForsakenBorders.Apollo.Tools.Updater.Packwiz
{
    public sealed record PackwizUpdateSystem
    {
        public PackwizUpdateSystemCurseforge? Curseforge { get; init; }
        public PackwizUpdateSystemModrinth? Modrinth { get; init; }
    }
}
