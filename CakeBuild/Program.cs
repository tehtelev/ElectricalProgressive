using System;
using System.IO;
using System.Linq;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.Restore;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;

public static class Program
{
    public static int Main(string[] args) => new CakeHost()
        .UseContext<BuildContext>()
        .UseWorkingDirectory("..")
        .Run(args);
}

public class BuildContext : FrostingContext
{
    public string[] ModProjects => new[] 
    {
        "ElectricalProgressive-Core",
        "ElectricalProgressive-Equipment",
        "ElectricalProgressive-QOL",
        "ElectricalProgressive-Basics"
    };

    public string OutputDir => "./ModReleases";
    public string Configuration { get; }
    public string Version { get; }

    public BuildContext(ICakeContext context) : base(context) 
    {
        Configuration = context.Arguments.GetArgument("configuration") ?? "Release";
        Version = GetModVersion(context);
    }

    private string GetModVersion(ICakeContext context)
    {
        try
        {
            var modInfo = context.DeserializeJsonFromFile<ModInfo>($"./ElectricalProgressive-Core/modinfo.json");
            return modInfo.Version ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }
}

public class ModInfo
{
    public string Version { get; set; }
}

[TaskName("Clean")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.CleanDirectory(context.OutputDir);
        
        foreach (var project in context.ModProjects)
        {
            var binDir = $"./{project}/bin";
            var objDir = $"./{project}/obj";
            
            if (context.DirectoryExists(binDir))
                context.CleanDirectory(binDir);
            
            if (context.DirectoryExists(objDir))
                context.CleanDirectory(objDir);
        }
    }
}

[TaskName("Restore")]
[IsDependentOn(typeof(CleanTask))]
public sealed class RestoreTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        foreach (var project in context.ModProjects)
        {
            var projPath = $"./{project}/{project}.csproj";
            
            if (!context.FileExists(projPath))
            {
                context.Log.Warning($"Project {project} not found");
                continue;
            }

            context.DotNetRestore(projPath);
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(RestoreTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        foreach (var project in context.ModProjects)
        {
            var projPath = $"./{project}/{project}.csproj";
            
            if (!context.FileExists(projPath))
            {
                context.Log.Warning($"Project {project} not found");
                continue;
            }

            context.DotNetBuild(projPath, new DotNetBuildSettings
            {
                Configuration = context.Configuration,
                NoRestore = true
            });
        }
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists(context.OutputDir);

        foreach (var project in context.ModProjects)
        {
            try
            {
                PackageSingleProject(context, project);
            }
            catch (Exception ex)
            {
                context.Log.Error($"Failed to package {project}: {ex.Message}");
            }
        }
    }

    private void PackageSingleProject(BuildContext context, string project)
    {
        // Создаем временную папку
        var tempDir = $"./temp_package_{project}";
        context.EnsureDirectoryExists(tempDir);
        context.CleanDirectory(tempDir);

        // Копируем DLL
        var dllPath = FindDllFile(context, project);
        if (dllPath != null)
        {
            context.CopyFile(dllPath, $"{tempDir}/{Path.GetFileName(dllPath)}");
        }

        // Копируем assets
        var assetsDir = $"./{project}/assets";
        if (context.DirectoryExists(assetsDir))
        {
            context.CopyDirectory(assetsDir, $"{tempDir}/assets");
        }

        // Копируем обязательные файлы
        CopyRequiredFiles(context, project, tempDir);

        // Создаем архив с версией
        var zipPath = $"{context.OutputDir}/{project}_{context.Version}.zip";
        context.Zip(tempDir, zipPath);
        context.Log.Information($"Created: {zipPath}");

        // Очищаем временную папку
        context.DeleteDirectory(tempDir, new DeleteDirectorySettings { Recursive = true });
    }

    private string FindDllFile(BuildContext context, string project)
    {
        var possiblePaths = new[]
        {
            $"./{project}/bin/{context.Configuration}/Mods/mod/{project}.dll",
            $"./{project}/bin/{context.Configuration}/{project}.dll",
            $"./{project}/bin/{project}.dll",
            $"./{project}/{project}.dll"
        };

        foreach (var path in possiblePaths)
        {
            if (context.FileExists(path))
            {
                context.Log.Debug($"Found DLL at: {path}");
                return path;
            }
        }

        context.Log.Warning($"DLL not found for {project}");
        return null;
    }

    private void CopyRequiredFiles(BuildContext context, string project, string targetDir)
    {
        var requiredFiles = new[]
        {
            "modinfo.json",
            "modicon.png",
            "README.md",
            "LICENSE"
        };

        foreach (var file in requiredFiles)
        {
            var sourcePath = $"./{project}/{file}";
            if (context.FileExists(sourcePath))
            {
                context.CopyFile(sourcePath, $"{targetDir}/{file}");
            }
        }
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask { }