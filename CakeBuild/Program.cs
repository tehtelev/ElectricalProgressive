using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace CakeBuild
{
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
        // ВАЖНО: порядок = порядок зависимостей (Core первым)
        public List<string> ProjectNames =
        [
            "ElectricalProgressive-Core",
            "ElectricalProgressive-Basics",
            "ElectricalProgressive-Equipment",
            "ElectricalProgressive-QOL",
            "ElectricalProgressive-Industry",
            "ElectricalProgressive-Transport",
            "ElectricalProgressive-Storage"
        ];

        // Проекты, которые успешно собрались (нужны для Package при ContinueOnError)
        public List<string> BuiltProjects { get; } = new();

        public BuildContext(ICakeContext context) : base(context)
        {
            BuildConfiguration = context.Argument("configuration", "Release");
            SkipJsonValidation = context.Argument("skipJsonValidation", false);
            ContinueOnError = context.Argument("continueOnError", false);
            Clean = context.Argument("clean", true); // можно отключить: --clean=false

            // Выборочная сборка: --project=ElectricalProgressive-QOL
            var only = context.Argument("project", "");
            if (!string.IsNullOrWhiteSpace(only))
                ProjectNames = ProjectNames.Where(p => p == only).ToList();
        }

        public string BuildConfiguration { get; set; }
        public bool SkipJsonValidation { get; set; }
        public bool ContinueOnError { get; set; }
        public bool Clean { get; set; }
    }

    [TaskName("CleanReleases")]
    public sealed class CleanReleasesTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            context.EnsureDirectoryExists("../Releases");
            context.CleanDirectory("../Releases");
        }
    }

    [TaskName("ValidateJson")]
    [IsDependentOn(typeof(CleanReleasesTask))]
    public sealed class ValidateJsonTask : FrostingTask<BuildContext>
    {
        public override bool ShouldRun(BuildContext context) => !context.SkipJsonValidation;

        public override void Run(BuildContext context)
        {
            var files = context.ProjectNames
                .SelectMany(p => context.GetFiles($"../{p}/assets/**/*.json"))
                .Select(f => f.FullPath)
                .ToList();

            var errors = new ConcurrentBag<string>();

            Parallel.ForEach(files, path =>
            {
                try
                {
                    JToken.Parse(File.ReadAllText(path));
                }
                catch (JsonException ex)
                {
                    errors.Add($"{path}: {ex.Message}");
                }
            });

            context.Information("Checked {0} JSON files", files.Count);

            if (!errors.IsEmpty)
            {
                throw new Exception("JSON validation failed:" + Environment.NewLine +
                                    string.Join(Environment.NewLine, errors.OrderBy(e => e)));
            }
        }
    }

    [TaskName("Build")]
    [IsDependentOn(typeof(ValidateJsonTask))]
    public sealed class BuildTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            // Строго последовательно: следующие проекты ссылаются на dll предыдущих
            foreach (var projectName in context.ProjectNames)
            {
                try
                {
                    var csprojPath = $"../{projectName}/{projectName}.csproj";

                    if (context.Clean)
                    {
                        context.Information("Cleaning {0}", projectName);
                        context.DotNetClean(csprojPath, new DotNetCleanSettings
                        {
                            Configuration = context.BuildConfiguration
                        });
                    }

                    context.Information("Publishing {0}", projectName);
                    context.DotNetPublish(csprojPath, new DotNetPublishSettings
                    {
                        Configuration = context.BuildConfiguration,
                        NoLogo = true,
                        MSBuildSettings = new DotNetMSBuildSettings()
                            .WithProperty("WarningLevel", "0")
                            .WithProperty("TreatWarningsAsErrors", "false")
                    });

                    context.BuiltProjects.Add(projectName);
                }
                catch (Exception ex)
                {
                    context.Error("Error building {0}: {1}", projectName, ex.Message);
                    if (!context.ContinueOnError) throw;
                    context.Warning("ContinueOnError is true — continuing to next project.");
                }
            }
        }
    }

    [TaskName("Package")]
    [IsDependentOn(typeof(BuildTask))]
    public sealed class PackageTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            var errors = new ConcurrentBag<string>();

            // Используем System.IO вместо Cake-алиасов — потокобезопаснее
            Parallel.ForEach(context.BuiltProjects, projectName =>
            {
                try
                {
                    PackProject(context, projectName);
                }
                catch (Exception ex)
                {
                    errors.Add($"{projectName}: {ex.Message}");
                }
            });

            if (!errors.IsEmpty)
            {
                var msg = "Packaging failed:" + Environment.NewLine +
                          string.Join(Environment.NewLine, errors);
                if (context.ContinueOnError) context.Error(msg);
                else throw new Exception(msg);
            }
        }

        private static void PackProject(BuildContext context, string projectName)
        {
            var modInfoPath = $"../{projectName}/modinfo.json";
            if (!File.Exists(modInfoPath))
                throw new FileNotFoundException($"modinfo.json not found for {projectName}", modInfoPath);

            var modInfo = JsonConvert.DeserializeObject<ModInfo>(File.ReadAllText(modInfoPath))
                          ?? throw new Exception("modinfo.json is empty");

            var releaseDir = Path.GetFullPath($"../Releases/{modInfo.ModID}");
            Directory.CreateDirectory(releaseDir);

            var flat = $"../{projectName}/bin/{context.BuildConfiguration}/Mods/publish";
            var nested = $"../{projectName}/bin/{context.BuildConfiguration}/Mods/mod/publish";
            var publishDir = Directory.Exists(flat) ? flat : nested;

            // Файлы из publish (как CopyFiles "publish/*" — только верхний уровень)
            foreach (var file in Directory.GetFiles(publishDir))
                File.Copy(file, Path.Combine(releaseDir, Path.GetFileName(file)), true);

            CopyDirectory($"../{projectName}/assets", Path.Combine(releaseDir, "assets"));
            File.Copy(modInfoPath, Path.Combine(releaseDir, "modinfo.json"), true);

            var iconPath = $"../{projectName}/modicon.png";
            if (File.Exists(iconPath))
                File.Copy(iconPath, Path.Combine(releaseDir, "modicon.png"), true);
            else
                context.Warning("modicon.png not found for {0}", projectName);

            var zipPath = Path.GetFullPath($"../Releases/{modInfo.ModID}_{modInfo.Version}.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(releaseDir, zipPath, CompressionLevel.Optimal, false);

            context.Information("Packed {0} -> {1}", projectName, Path.GetFileName(zipPath));
        }

        private static void CopyDirectory(string source, string target)
        {
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, target));

            Directory.CreateDirectory(target);

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(source, target), true);
        }
    }

    [TaskName("Default")]
    [IsDependentOn(typeof(PackageTask))]
    public class DefaultTask : FrostingTask
    {
    }

    public class ModInfo
    {
        public string ModID { get; set; }
        public string Version { get; set; }
    }
}