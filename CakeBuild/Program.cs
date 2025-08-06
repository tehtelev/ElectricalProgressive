using System;
using System.IO;
using System.Collections.Generic;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

public static class Program {
    public static int Main(string[] args) {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext {
    public List<string> ProjectNames = new List<string> { 
        "ElectricalProgressive-Core",
        "ElectricalProgressive-Basics",
        "ElectricalProgressive-Equipment",
        "ElectricalProgressive-QOL",
        "ElectricalProgressive-Industry",
        
        // Add other project names here
    };

    public BuildContext(ICakeContext context) : base(context) {
        this.BuildConfiguration = context.Argument("configuration", "Release");
        this.SkipJsonValidation = context.Argument("skipJsonValidation", false);
    }

    public string BuildConfiguration { get; set; }
    public bool SkipJsonValidation { get; set; }
}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext> {
    public override void Run(BuildContext context) {
        if (context.SkipJsonValidation) {
            return;
        }

        foreach (var projectName in context.ProjectNames) {
            var jsonFiles = context.GetFiles($"../{projectName}/assets/**/*.json");

            foreach (var file in jsonFiles) {
                try {
                    var json = File.ReadAllText(file.FullPath);
                    JToken.Parse(json);
                }
                catch (JsonException ex) {
                    throw new Exception($"Validation failed for JSON file in project {projectName}: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
                }
            }
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext> {
    public override void Run(BuildContext context) {
        foreach (var projectName in context.ProjectNames) {
            context.DotNetClean(
                $"../{projectName}/{projectName}.csproj",
                new DotNetCleanSettings {
                    Configuration = context.BuildConfiguration
                }
            );

            context.DotNetPublish(
                $"../{projectName}/{projectName}.csproj",
                new DotNetPublishSettings {
                    Configuration = context.BuildConfiguration
                }
            );
        }
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext> {
    public override void Run(BuildContext context) {
        context.EnsureDirectoryExists("../Releases");
        context.CleanDirectory("../Releases");

        foreach (var projectName in context.ProjectNames) {
            var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{projectName}/modinfo.json");
            var version = modInfo.Version;
            var name = modInfo.ModID;

            context.EnsureDirectoryExists($"../Releases/{name}");
            context.CopyFiles($"../{projectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/*", $"../Releases/{name}");
            context.CopyDirectory($"../{projectName}/assets", $"../Releases/{name}/assets");
            context.CopyFile($"../{projectName}/modinfo.json", $"../Releases/{name}/modinfo.json");
            context.CopyFile($"../{projectName}/modicon.png", $"../Releases/{name}/modicon.png");
            context.Zip($"../Releases/{name}", $"../Releases/{name}_{version}.zip");
        }
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask {
}