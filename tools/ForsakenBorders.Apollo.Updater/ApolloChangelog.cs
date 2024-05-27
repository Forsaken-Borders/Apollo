using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using OoLunar.ForsakenBorders.Apollo.Updater.Packwiz;

namespace OoLunar.ForsakenBorders.Apollo.Updater
{
    public sealed record ApolloChangelog
    {
        public IReadOnlyList<PackwizEntry> Before { get; init; }
        public IReadOnlyList<PackwizEntry> After { get; init; }
        public IReadOnlyList<PackwizEntry> NewMods { get; init; }
        public IReadOnlyList<PackwizEntry> RemovedMods { get; init; }
        public IReadOnlyDictionary<PackwizEntry, (string oldVersion, string newVersion)> UpdatedMods { get; init; }
        public Version OldModpackVersion { get; init; }
        public Version NewModpackVersion { get; init; }

        [SuppressMessage("Roslyn", "IDE0045", Justification = "Ternary rabbit hole.")]
        public ApolloChangelog(Version currentModpackVersion, IReadOnlyList<PackwizEntry> before, IReadOnlyList<PackwizEntry> after)
        {
            Before = before;
            After = after;
            OldModpackVersion = currentModpackVersion;
            RemovedMods = before.Where(oldEntry => !after.Any(newEntry => newEntry.Name == oldEntry.Name)).ToList();
            NewMods = after.Where(newEntry => !before.Any(oldEntry => oldEntry.Name == newEntry.Name)).ToList();

            bool minorBump = NewMods.Count > 0 || RemovedMods.Count > 0;
            bool patchBump = false;
            Dictionary<PackwizEntry, (string oldVersion, string newVersion)> updatedMods = [];
            foreach (PackwizEntry oldEntry in before)
            {
                foreach (PackwizEntry newEntry in after)
                {
                    if (oldEntry.Name != newEntry.Name)
                    {
                        continue;
                    }
                    else if (TryParseVersion(oldEntry.Filename, out Version? currentVersion, out string? currentVersionSuffix) && TryParseVersion(newEntry.Filename, out Version? newVersion, out string? newVersionSuffix))
                    {
                        if (currentVersion.Major != newVersion.Major || currentVersion.Minor != newVersion.Minor)
                        {
                            updatedMods.Add(oldEntry, ($"{currentVersion}{currentVersionSuffix}", $"{newVersion}{newVersionSuffix}"));
                            minorBump = true;
                        }
                        else if (currentVersion.Build != newVersion.Build || !currentVersionSuffix.Equals(newVersionSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            updatedMods.Add(oldEntry, ($"{currentVersion}{currentVersionSuffix}", $"{newVersion}{newVersionSuffix}"));
                            patchBump = true;
                        }
                    }
                    else if (!oldEntry.Filename.Equals(newEntry.Filename, StringComparison.OrdinalIgnoreCase))
                    {
                        updatedMods.Add(oldEntry, (oldEntry.Filename, newEntry.Filename));
                        minorBump = true;
                    }
                }
            }

            UpdatedMods = updatedMods;
            string? explicitVersion = Environment.GetEnvironmentVariable("MODPACK_VERSION");
            if (!string.IsNullOrWhiteSpace(explicitVersion) && Version.TryParse(explicitVersion, out Version? explicitParsedVersion))
            {
                NewModpackVersion = explicitParsedVersion;
                return;
            }
            else if (minorBump)
            {
                NewModpackVersion = new Version(currentModpackVersion.Major, currentModpackVersion.Minor + 1, 0);
            }
            else if (patchBump)
            {
                NewModpackVersion = new Version(currentModpackVersion.Major, currentModpackVersion.Minor, currentModpackVersion.Build + 1);
            }
            else
            {
                NewModpackVersion = currentModpackVersion;
            }
        }

        private static bool TryParseVersion(string version, [NotNullWhen(true)] out Version? parsedVersion, [NotNullWhen(true)] out string? suffix)
        {
            foreach (string part in Path.GetFileNameWithoutExtension(version).Split('-', '_'))
            {
                ReadOnlySpan<char> versionSpan = part.AsSpan();
                versionSpan = versionSpan.TrimStart("vV");
                int firstIndex = versionSpan.IndexOf('.');
                if (firstIndex == -1)
                {
                    continue;
                }

                int secondIndex = firstIndex + 1 + versionSpan[(firstIndex + 1)..].IndexOf('.');
                if (secondIndex == -1)
                {
                    continue;
                }

                int lastIndex = secondIndex + 1 + versionSpan[(secondIndex + 1)..].IndexOf('.');
                if (lastIndex == secondIndex)
                {
                    lastIndex = versionSpan.Length;
                }

                // Ensure the substring is a valid version and not listing the game version (1.19)
                if (Version.TryParse(versionSpan[..lastIndex], out parsedVersion) && parsedVersion.Major != 1 && parsedVersion.Minor != 19)
                {
                    suffix = versionSpan[lastIndex..].ToString();
                    return true;
                }
            }

            suffix = null;
            parsedVersion = null;
            return false;
        }
    }
}
