// ReSharper disable RedundantUsingDirective

using System;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.OctoVersion;
using Nuke.Common.Utilities.Collections;
using Serilog;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    const string CiBranchNameEnvVariable = "OCTOVERSION_CurrentBranch";

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")] readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;

    [Parameter("Whether to auto-detect the branch name - this is okay for a local build, but should not be used under CI.")] readonly bool AutoDetectBranch = IsLocalBuild;

    [OctoVersion(Framework = "net8.0",
        BranchMember = nameof(BranchName),
        AutoDetectBranchMember = nameof(AutoDetectBranch))]
    public OctoVersionInfo OctoVersionInfo;

    [Parameter("Branch name for OctoVersion to use to calculate the version number. Can be set via the environment variable " + CiBranchNameEnvVariable + ".", Name = CiBranchNameEnvVariable)]
    string BranchName { get; set; }

    AbsolutePath SourceDirectory => RootDirectory / "source";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath LocalPackagesDirectory => RootDirectory / ".." / "LocalPackages";

    Target Clean => t => t
        .Before(Restore)
        .Executes(() =>
        {
            foreach (var dir in SourceDirectory.GlobDirectories("**/bin", "**/obj", "**/TestResults"))
                dir.DeleteDirectory();

            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target CalculateVersion => t => t
        .Executes(() =>
        {
            //all the magic happens inside `[NukeOctoVersion]` above. we just need a target for TeamCity to call
        });

    Target Restore => t => t
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(t => t
                .SetProjectFile(Solution));
        });

    Target Compile => t => t
        .DependsOn(Clean)
        .DependsOn(Restore)
        .Executes(() =>
        {
            Log.Information("Building Octopus.Shellfish v{0}", OctoVersionInfo.FullSemVer);

            DotNetBuild(t => t
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetVersion(OctoVersionInfo.FullSemVer)
                .EnableNoRestore());
        });

    Target Test => t => t
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(t => t
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .EnableNoRestore());
        });

    Target Pack => t => t
        .DependsOn(Compile)
        .DependsOn(Test)
        .Executes(() =>
        {
            DotNetPack(t => t
                .SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .EnableNoBuild()
                .AddProperty("Version", OctoVersionInfo.FullSemVer)
            );
        });

    Target CopyToLocalPackages => t => t
        .OnlyWhenStatic(() => IsLocalBuild)
        .TriggeredBy(Pack)
        .Executes(() =>
        {
            LocalPackagesDirectory.CreateDirectory();

            var pkg = ArtifactsDirectory / $"Octopus.Shellfish.{OctoVersionInfo.FullSemVer}.nupkg";
            pkg.CopyToDirectory(LocalPackagesDirectory, ExistsPolicy.FileOverwrite);
        });

    /// Support plugins are available for:
    /// - JetBrains ReSharper        https://nuke.build/resharper
    /// - JetBrains Rider            https://nuke.build/rider
    /// - Microsoft VisualStudio     https://nuke.build/visualstudio
    /// - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.Pack);
}