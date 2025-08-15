using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    public List<string> ProjectNames = new List<string> {
        "ElectricalProgressive-Core",
        "ElectricalProgressive-Basics",
        "ElectricalProgressive-Equipment",
        "ElectricalProgressive-QOL",
        "ElectricalProgressive-Industry",
        // Add other project names here
    };

    public BuildContext(ICakeContext context) : base(context)
    {
        this.BuildConfiguration = context.Argument("configuration", "Release");
        this.SkipJsonValidation = context.Argument("skipJsonValidation", false);
        // Optionally allow continuing on error; default false to fail fast
        this.ContinueOnError = context.Argument("continueOnError", false);
    }

    public string BuildConfiguration { get; set; }
    public bool SkipJsonValidation { get; set; }
    public bool ContinueOnError { get; set; }
}

[TaskName("PerProject")]
public sealed class PerProjectTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // Ensure releases directory exists and is clean only once before the per-project loop
        context.EnsureDirectoryExists("../Releases");
        context.CleanDirectory("../Releases");

        foreach (var projectName in context.ProjectNames)
        {
            context.Information("=== Processing project: {0} ===", projectName);

            try
            {
                // 1) Validate JSON for this project (unless skipped)
                if (!context.SkipJsonValidation)
                {
                    var jsonFiles = context.GetFiles($"../{projectName}/assets/**/*.json");
                    foreach (var file in jsonFiles)
                    {
                        try
                        {
                            var json = File.ReadAllText(file.FullPath);
                            JToken.Parse(json);
                        }
                        catch (JsonException ex)
                        {
                            throw new Exception($"Validation failed for JSON file in project {projectName}: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
                        }
                    }
                    context.Information("JSON validation passed for {0}", projectName);
                }
                else
                {
                    context.Information("Skipping JSON validation for {0}", projectName);
                }

                // 2) Clean and publish this project
                var csprojPath = $"../{projectName}/{projectName}.csproj";
                context.Information("Cleaning project {0}", projectName);
                context.DotNetClean(csprojPath, new DotNetCleanSettings
                {
                    Configuration = context.BuildConfiguration
                });

                context.Information("Publishing project {0}", projectName);
                context.DotNetPublish(csprojPath, new DotNetPublishSettings
                {
                    Configuration = context.BuildConfiguration
                });

                // 3) Package this project into Releases/{ModID}_{version}.zip
                // Read modinfo.json for version and mod id
                var modInfoPath = $"../{projectName}/modinfo.json";
                if (!File.Exists(modInfoPath))
                {
                    throw new FileNotFoundException($"modinfo.json not found for project {projectName}", modInfoPath);
                }

                var modInfo = context.DeserializeJsonFromFile<ModInfo>(modInfoPath);
                var version = modInfo.Version;
                var name = modInfo.ModID;

                var releaseDir = $"../Releases/{name}";
                context.EnsureDirectoryExists(releaseDir);

                // Copy published files
                var publishSource = $"../{projectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/*";
                context.Information("Copying published files from {0} to {1}", publishSource, releaseDir);
                context.CopyFiles(publishSource, releaseDir);

                // Copy assets and metadata
                context.CopyDirectory($"../{projectName}/assets", $"{releaseDir}/assets");
                context.CopyFile($"../{projectName}/modinfo.json", $"{releaseDir}/modinfo.json");

                var iconPath = $"../{projectName}/modicon.png";
                if (File.Exists(iconPath))
                {
                    context.CopyFile(iconPath, $"{releaseDir}/modicon.png");
                }
                else
                {
                    context.Warning("modicon.png not found for project {0}, skipping icon copy.", projectName);
                }

                // Zip the release folder
                var zipName = $"../Releases/{name}_{version}.zip";
                context.Information("Zipping {0} -> {1}", releaseDir, zipName);
                context.Zip(releaseDir, zipName);

                context.Information("Project {0} processed successfully.\n", projectName);
            }
            catch (Exception ex)
            {
                context.Error("Error processing project {0}: {1}", projectName, ex.Message);
                if (!context.ContinueOnError)
                {
                    // Fail fast: rethrow to stop the build host
                    throw;
                }
                else
                {
                    // Log and continue with next project
                    context.Warning("ContinueOnError is true — continuing to next project.");
                }
            }
        }
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PerProjectTask))]
public class DefaultTask : FrostingTask
{
}

// Note: ModInfo class must be present somewhere in the codebase (same as before).
// If not, define a minimal class here:
public class ModInfo
{
    public string ModID { get; set; }
    public string Version { get; set; }
}
