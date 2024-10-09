namespace ForsakenBorders.Apollo.Tools.Updater.Packwiz
{
    public sealed record PackwizIndexFile
    {
        public string File { get; init; } = null!;
        public string Hash { get; init; } = null!;
        public bool MetaFile { get; init; }
    }
}
