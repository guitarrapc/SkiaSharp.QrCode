#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

if (args.Length != 1 || args[0] is not ("major" or "minor" or "patch"))
{
    PrintUsage();
    return 1;
}

var bumpKind = args[0];
var repoRoot = GetRepoRoot();
var latestTag = GetLatestTag(repoRoot);
var current = ParseTag(latestTag);
var next = Bump(current, bumpKind);
var currentText = current.ToString();
var nextText = next.ToString();
var propsPath = Path.Combine(repoRoot, "Directory.Build.props");
var readmePath = Path.Combine(repoRoot, "README.md");

Console.WriteLine($"Latest tag: {latestTag}");
Console.WriteLine($"Next version: {nextText} ({bumpKind})");
Console.WriteLine();

var originalProps = File.ReadAllText(propsPath);
var originalReadme = File.ReadAllText(readmePath);
var updatedProps = ReplacePropsVersion(originalProps, currentText, nextText);
var updatedReadme = ReplaceReadmeVersions(originalReadme, currentText, nextText);

var changedFiles = new List<string>();
WriteFileIfChanged(propsPath, originalProps, updatedProps, changedFiles, repoRoot);
WriteFileIfChanged(readmePath, originalReadme, updatedReadme, changedFiles, repoRoot);

Console.WriteLine();
Console.WriteLine(changedFiles.Count == 0
    ? $"Version files are already at {nextText}."
    : $"Updated {changedFiles.Count} file(s) to {nextText}.");

return 0;

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: dotnet ./tools/bump_version.cs <major|minor|patch>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Reads the latest X.Y.Z or vX.Y.Z git tag, bumps the version, and updates");
    Console.Error.WriteLine("  Directory.Build.props and the PackageReference examples in README.md (FeatherQR,");
    Console.Error.WriteLine("  FeatherQR.SkiaSharp and the SkiaSharp.QrCode metapackage). Prerelease versions such as");
    Console.Error.WriteLine("  2.0.0-preview.1 are edited by hand; this tool only produces X.Y.Z.");
}

static string GetRepoRoot()
{
    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Repository root not found (.git missing). Run from inside the repository.");
}

static string GetLatestTag(string repoRoot)
{
    var output = RunGit(repoRoot, "tag", "--sort=-version:refname");
    foreach (var tag in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (IsSemverTag(tag, out _))
            return tag;
    }

    throw new InvalidOperationException("No semver git tag found (expected X.Y.Z or vX.Y.Z).");
}

static Version ParseTag(string tag)
{
    if (IsSemverTag(tag, out var version))
        return version;

    throw new InvalidOperationException($"Failed to parse tag: {tag}");
}

static bool IsSemverTag(string tag, out Version version)
{
    var match = Regex.Match(tag, @"^v?(?<version>\d+\.\d+\.\d+)$", RegexOptions.CultureInvariant);
    if (match.Success && Version.TryParse(match.Groups["version"].Value, out version!))
        return true;

    version = default!;
    return false;
}

static Version Bump(Version current, string kind) => kind switch
{
    "major" => new Version(current.Major + 1, 0, 0),
    "minor" => new Version(current.Major, current.Minor + 1, 0),
    "patch" => new Version(current.Major, current.Minor, current.Build + 1),
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
};

static string ReplacePropsVersion(string text, string current, string next)
{
    var currentValue = $"<Version>{current}</Version>";
    var nextValue = $"<Version>{next}</Version>";
    if (text.Contains(currentValue, StringComparison.Ordinal))
        return text.Replace(currentValue, nextValue, StringComparison.Ordinal);

    if (text.Contains(nextValue, StringComparison.Ordinal))
        return text;

    throw new InvalidOperationException($"Directory.Build.props contains neither version {current} nor {next}.");
}

// The three packages ship at one lockstep version, so every install line in the README moves together.
static readonly string[] PackageIds = ["FeatherQR", "FeatherQR.SkiaSharp", "SkiaSharp.QrCode"];

static string ReplaceReadmeVersions(string text, string current, string next)
{
    var replaced = false;
    var alreadyNext = false;
    foreach (var id in PackageIds)
    {
        var currentReference = $"<PackageReference Include=\"{id}\" Version=\"{current}\" />";
        var nextReference = $"<PackageReference Include=\"{id}\" Version=\"{next}\" />";
        if (text.Contains(currentReference, StringComparison.Ordinal))
        {
            text = text.Replace(currentReference, nextReference, StringComparison.Ordinal);
            replaced = true;
        }
        else if (text.Contains(nextReference, StringComparison.Ordinal))
        {
            alreadyNext = true;
        }
    }

    if (replaced || alreadyNext)
        return text;

    throw new InvalidOperationException($"README.md contains no PackageReference ({string.Join(", ", PackageIds)}) for version {current} or {next}.");
}

static void WriteFileIfChanged(string path, string original, string updated, List<string> changedFiles, string repoRoot)
{
    if (updated == original)
        return;

    File.WriteAllText(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    changedFiles.Add(Path.GetRelativePath(repoRoot, path));
    Console.WriteLine(Path.GetRelativePath(repoRoot, path));
}

static string RunGit(string repoRoot, params string[] arguments)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        },
    };

    foreach (var argument in arguments)
        process.StartInfo.ArgumentList.Add(argument);

    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
        throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr.Trim()}");

    return stdout;
}
