using System.Collections.Generic;

namespace ForsakenBorders.Apollo.Tools.Updater.Packwiz
{
    public sealed record PackwizIndex
    {
        public string HashFormat { get; init; } = null!;
        public List<PackwizIndexFile> Files { get; init; } = null!;
    }
}
