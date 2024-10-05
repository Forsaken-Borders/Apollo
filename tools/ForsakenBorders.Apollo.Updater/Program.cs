using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Humanizer;
using OoLunar.ForsakenBorders.Apollo.Updater.Packwiz;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.SystemConsole.Themes;
using Tomlyn;
using Tomlyn.Syntax;

namespace OoLunar.ForsakenBorders.Apollo.Updater
{
    public sealed class Program
    {
        private const string INDEX_FILE_NAME = "index.toml";
        private const string PACK_FILE_NAME = "pack.toml";
        private const string CHANGELOG_FILE_NAME = "CHANGELOG.md";

        private static readonly string PackwizBinary = Environment.GetEnvironmentVariable("PACKWIZ_BINARY") ?? "packwiz";
        private static readonly string GitBinary = Environment.GetEnvironmentVariable("GIT_BINARY") ?? "git";

        public static async Task<int> Main()
        {
            LoggingDefaults loggingDefaults = new();
            LoggerConfiguration serilogLoggerConfiguration = new();
            serilogLoggerConfiguration.MinimumLevel.Is(loggingDefaults.LogLevel);
            serilogLoggerConfiguration.WriteTo.Console(
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: loggingDefaults.Format,
                theme: AnsiConsoleTheme.Code
            );

            // Create the console logger
            Logger logger = serilogLoggerConfiguration.CreateLogger();

            // Ensure the packwiz CLI is installed
            (string output, int exitCode) = await ExecuteProgramAsync(PackwizBinary, "--help", logger);
            if (exitCode != 0)
            {
                logger.Fatal("Failed to locate packwiz: {Output}", output);
                return exitCode;
            }

            // Ensure the git CLI is installed
            (output, exitCode) = await ExecuteProgramAsync(GitBinary, "--version", logger);
            if (exitCode != 0)
            {
                logger.Fatal("Failed to locate git: {Output}", output);
                return exitCode;
            }

            // Change current directory to the src folder
            Directory.SetCurrentDirectory(Path.Combine(ThisAssembly.Project.ProjectRoot, "src"));

            // Fetch the latest remote commits
            (output, exitCode) = await ExecuteProgramAsync(GitBinary, "fetch --all --tags", logger);
            if (exitCode != 0)
            {
                logger.Error("Failed to fetch latest commit: {Output}", output);
                return exitCode;
            }

            // Grab the latest commit here instead of when we're checked out on a previous tag
            (string latestCommit, exitCode) = await ExecuteProgramAsync(GitBinary, "rev-parse HEAD", logger);
            if (string.IsNullOrWhiteSpace(latestCommit) || exitCode != 0)
            {
                logger.Error("Failed to get the latest commit: {Output}", latestCommit);
                return exitCode;
            }

            // Try to get the previous tag
            (string latestTag, exitCode) = await ExecuteProgramAsync(GitBinary, $"describe --tags --abbrev=0", logger);
            if (string.IsNullOrWhiteSpace(latestTag) || exitCode != 0)
            {
                logger.Error("Failed to get the latest tag: {Output}", latestTag);
                return exitCode;
            }

            // Parse the old state of the modpack
            (output, exitCode) = await ExecuteProgramAsync(GitBinary, $"checkout {latestTag} .", logger);
            if (exitCode != 0)
            {
                logger.Error("Failed to checkout latest tag: {Output}", output);
                return exitCode;
            }

            IReadOnlyList<PackwizEntry> oldEntries = await GrabPackwizEntriesAsync(logger);

            // Parse the new state of the modpack
            (output, exitCode) = await ExecuteProgramAsync(GitBinary, $"checkout {latestCommit} .", logger);
            if (exitCode != 0)
            {
                // Try checking out the latest tag instead
                logger.Warning("Checking out latest tag due to failure to checkout latest commit: {Output}", output);
                (output, exitCode) = await ExecuteProgramAsync(GitBinary, $"checkout {latestTag} .", logger);
                if (exitCode != 0)
                {
                    logger.Error("Failed to checkout latest tag: {Output}", output);
                    return exitCode;
                }
            }

            // Update the modpack
            (output, exitCode) = await ExecuteProgramAsync(PackwizBinary, $"update --all -y", logger);
            if (exitCode != 0)
            {
                logger.Error("Failed to update modpack through packwiz: {Output}", output);
                return exitCode;
            }

            // Parse the new state of the modpack
            Version modpackVersion = await GrabModpackVersionAsync(logger);
            IReadOnlyList<PackwizEntry> newEntries = await GrabPackwizEntriesAsync(logger);

            // Print the changelog to console and update the modpack version
            await GenerateChangelogAsync(modpackVersion, oldEntries, newEntries, logger);

            // Try exporting the modpack
            await FileManager.PackModpackAsync(logger);
            return 0;
        }

        private static async ValueTask<Version> GrabModpackVersionAsync(Logger logger)
        {
            // Parse the index.toml file
            DocumentSyntax syntax = Toml.Parse(await File.ReadAllBytesAsync(PACK_FILE_NAME), PACK_FILE_NAME, TomlParserOptions.ParseOnly);
            if (syntax.HasErrors)
            {
                Environment.Exit(LogAndExit(logger, syntax.Diagnostics));
                return null;
            }
            else if (!syntax.TryToModel(out PackwizPack? pack, out DiagnosticsBag diagnostics, new TomlModelOptions()
            {
                ConvertPropertyName = InflectorExtensions.Kebaberize,
                IgnoreMissingProperties = true
            }))
            {
                Environment.Exit(LogAndExit(logger, diagnostics));
                return null;
            }
            else
            {
                return Version.Parse(pack.Version);
            }
        }

        private static async ValueTask<IReadOnlyList<PackwizEntry>> GrabPackwizEntriesAsync(Logger logger)
        {
            // Parse the index.toml file
            DocumentSyntax syntax = Toml.Parse(await File.ReadAllBytesAsync(INDEX_FILE_NAME), INDEX_FILE_NAME, TomlParserOptions.ParseOnly);
            if (syntax.HasErrors)
            {
                Environment.Exit(LogAndExit(logger, syntax.Diagnostics));
                return null;
            }
            else if (!syntax.TryToModel(out PackwizIndex? index, out DiagnosticsBag diagnostics, new TomlModelOptions()
            {
                ConvertPropertyName = InflectorExtensions.Kebaberize,
                IgnoreMissingProperties = true
            }))
            {
                Environment.Exit(LogAndExit(logger, diagnostics));
                return null;
            }
            else
            {
                // Iterate through the index and store the current version of the mods
                List<PackwizEntry> currentVersions = [];
                foreach (PackwizIndexFile mod in index.Files)
                {
                    if (!mod.File.StartsWith("mods/", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.Debug("Skipping {Mod} as it is not a mod.", mod.File);
                        continue;
                    }

                    // Parse the file
                    string filepath = Path.Combine(ThisAssembly.Project.ProjectRoot, "src", mod.File);
                    DocumentSyntax modSyntax = Toml.Parse(await File.ReadAllBytesAsync(filepath), filepath, TomlParserOptions.ParseOnly);
                    if (modSyntax.HasErrors)
                    {
                        Environment.Exit(LogAndExit(logger, modSyntax.Diagnostics));
                        return null;
                    }
                    else if (!modSyntax.TryToModel(out PackwizEntry? entry, out DiagnosticsBag modDiagnostics, new TomlModelOptions()
                    {
                        ConvertPropertyName = InflectorExtensions.Kebaberize,
                        IgnoreMissingProperties = true
                    }))
                    {
                        Environment.Exit(LogAndExit(logger, modDiagnostics));
                        return null;
                    }
                    else
                    {
                        currentVersions.Add(entry);
                    }
                }

                return currentVersions;
            }
        }

        private static int LogAndExit(Logger logger, DiagnosticsBag diagnostics)
        {
            logger.Error("Failed to parse TOML file: {TomlFilepath}", INDEX_FILE_NAME);
            foreach (DiagnosticMessage error in diagnostics)
            {
                if (error.Kind is DiagnosticMessageKind.Error)
                {
                    logger.Error("{Error}", error.Message);
                }
                else
                {
                    logger.Warning("{Warning}", error.Message);
                }
            }

            return 1;
        }

        public static async ValueTask<(string output, int exitCode)> ExecuteProgramAsync(string command, string args, Logger logger)
        {
            Process process = new()
            {
                StartInfo = new()
                {
                    FileName = command,
                    Arguments = args,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            logger.Information("Executing: {Command} {Args}", command, args);
            try
            {
                process.Start();

                CancellationTokenSource source = new(TimeSpan.FromMinutes(2));
                await process.WaitForExitAsync(source.Token);
            }
            catch (Exception error)
            {
                logger.Error("Failed to execute {Command} {Args}: {Error}", command, args, error.Message);
                if (!process.HasExited)
                {
                    process.Kill();
                    logger.Warning("Killed {Command} {Args}", command, args);
                }
            }

            StringBuilder result = new();
            if (process.StandardOutput.Peek() > -1)
            {
                result.AppendLine((await process.StandardOutput.ReadToEndAsync()).Trim());
            }

            if (process.StandardError.Peek() > -1)
            {
                result.AppendLine((await process.StandardError.ReadToEndAsync()).Trim());
            }

            logger.Debug("Exit Code: {ExitCode}", process.ExitCode);
            logger.Debug("Output: {Output}", result);
            return (result.ToString().Trim(), process.ExitCode);
        }

        private static async ValueTask GenerateChangelogAsync(Version modpackVersion, IReadOnlyList<PackwizEntry> oldEntries, IReadOnlyList<PackwizEntry> newEntries, Logger logger)
        {
            // Generate the changelog
            ApolloChangelog changelog = new(modpackVersion, oldEntries, newEntries);
            StringBuilder stringBuilder = new("# Apollo Changelog\n");
            stringBuilder.AppendLine($"## Version {changelog.NewModpackVersion}");

            // Copy the changelog to a string for comparison later
            string emptyChangelog = stringBuilder.ToString();

            // Log the changelog
            if (changelog.NewMods.Count == 0)
            {
                logger.Information("No new mods were added.");
            }
            else if (changelog.NewMods.Count == 1)
            {
                logger.Information("New Mod: {NewMods}", changelog.NewMods[0].Name);
                stringBuilder.AppendLine("## New Mods");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine($"- `{changelog.NewMods[0].Name}`");
            }
            else
            {
                logger.Information("New Mods ({NewModCount:N0}): {NewMods}", changelog.NewMods.Count, string.Join(", ", changelog.NewMods.Select(mod => mod.Name)));
                stringBuilder.AppendLine("## New Mods");
                stringBuilder.AppendLine();
                foreach (PackwizEntry mod in changelog.NewMods)
                {
                    stringBuilder.AppendLine($"- `{mod.Name}`");
                }
            }

            if (changelog.RemovedMods.Count == 0)
            {
                logger.Information("No mods were removed.");
            }
            else if (changelog.RemovedMods.Count == 1)
            {
                logger.Information("Removed Mod: {RemovedMods}", changelog.RemovedMods[0].Name);
                stringBuilder.AppendLine("## Removed Mods");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine($"- `{changelog.RemovedMods[0].Name}`");
            }
            else
            {
                logger.Information("Removed Mods ({RemovedModCount:N0}): {RemovedMods}", changelog.RemovedMods.Count, string.Join(", ", changelog.RemovedMods.Select(mod => mod.Name)));
                stringBuilder.AppendLine("## Removed Mods");
                stringBuilder.AppendLine();
                foreach (PackwizEntry mod in changelog.RemovedMods)
                {
                    stringBuilder.AppendLine($"- `{mod.Name}`");
                }
            }

            if (changelog.UpdatedMods.Count == 0)
            {
                logger.Information("No mods were updated.");
            }
            else if (changelog.UpdatedMods.Count == 1)
            {
                (PackwizEntry mod, (string oldVersion, string newVersion)) = changelog.UpdatedMods.First();
                logger.Information("Updated Mod: {Mod} was updated from {OldVersion} to {NewVersion}", mod.Name, oldVersion, newVersion);
                stringBuilder.AppendLine("## Updated Mods");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine($"- `{mod.Name}` was updated from `{oldVersion}` to `{newVersion}`");
            }
            else
            {
                logger.Information("Updated Mods ({UpdatedMods:N0}):", changelog.UpdatedMods.Count);
                stringBuilder.AppendLine("## Updated Mods");
                stringBuilder.AppendLine();
                foreach ((PackwizEntry mod, (string oldVersion, string newVersion) versions) in changelog.UpdatedMods)
                {
                    logger.Information("- {Mod} was updated from {OldVersion} to {NewVersion}", mod.Name, versions.oldVersion, versions.newVersion);
                    stringBuilder.AppendLine($"- `{mod.Name}` was updated from `{versions.oldVersion}` to `{versions.newVersion}`");
                }
            }

            // Ensure we're not writing an empty changelog
            if (stringBuilder.ToString() == emptyChangelog)
            {
                stringBuilder.AppendLine("No changes were made to the modpack.");
            }

            // Write the changelog to disk
            await File.WriteAllTextAsync(Path.Join(ThisAssembly.Project.ProjectRoot, CHANGELOG_FILE_NAME), stringBuilder.ToString());

            // Bump the modpack version
            if (changelog.OldModpackVersion == changelog.NewModpackVersion)
            {
                logger.Information("No changes were made to the modpack version.");
                return;
            }

            // Update the modpack version
            DocumentSyntax syntax = Toml.Parse(await File.ReadAllBytesAsync(PACK_FILE_NAME), PACK_FILE_NAME, TomlParserOptions.ParseOnly);
            foreach (KeyValueSyntax child in syntax.KeyValues)
            {
                if (child.Key?.ToString().Trim() == "version")
                {
                    // Update the version
                    child.Value = new StringValueSyntax(changelog.NewModpackVersion.ToString());

                    // Save the file
                    StreamWriter writer = new(File.OpenWrite(PACK_FILE_NAME), leaveOpen: false);
                    syntax.WriteTo(writer);
                    await writer.DisposeAsync();

                    // Update the hashes
                    await ExecuteProgramAsync(PackwizBinary, "refresh", logger);

                    // Log and exit
                    logger.Information("Modpack updated from {OldVersion} to {NewVersion}", changelog.OldModpackVersion, changelog.NewModpackVersion);
                    return;
                }
            }

            // This presumably happened when the version key was not found
            logger.Error("Failed to bump modpack version!");
            return;
        }
    }
}
