using System.IO.Abstractions;
using WheelWizard.ApplicationData;

namespace WheelWizard.GameFiles;

/// <summary>
/// Where the extracted game lives. The layout is the one Dolphin writes with "Extract Entire Disc" and boots from
/// <c>DATA/sys/main.dol</c>; WiiCompiled reads the same <c>DATA</c> folder.
/// </summary>
public interface IGameFilesPaths
{
    /// <summary>The extracted game: <c>disc/</c> and <c>DATA/</c>. May be a link the user made to another drive.</summary>
    string RootPath { get; }

    string DataPath { get; }
    string MainDolPath { get; }
    string StaticRelPath { get; }
    string BootBinPath { get; }

    /// <summary>Written last, so an extraction without it never finished.</summary>
    string ManifestPath { get; }

    /// <summary>
    /// The folder <see cref="RootPath"/> really is: the link's target when the user linked it elsewhere, which is where
    /// files are written and replaced so the link itself is never touched.
    /// </summary>
    string ResolvedRootPath { get; }

    /// <summary>Whether <see cref="RootPath"/> is a symbolic link or junction.</summary>
    bool IsLinked { get; }

    /// <summary>Where a new extraction is written before it takes the place of the current one. Same drive as the target.</summary>
    string StagingPath { get; }

    /// <summary>Where a replaced extraction waits to be deleted, so a crash mid-swap never loses both.</summary>
    string ReplacedPath { get; }
}

public sealed class GameFilesPaths(IApplicationDataLocation applicationData, IFileSystem fileSystem) : IGameFilesPaths
{
    public const string FolderName = "GAME";
    public const string ManifestFileName = ".wheelwizard-game.json";

    /// <summary>Marks a folder as Wheel Wizard's own unfinished or replaced work, which it may always delete.</summary>
    public const string WorkMarkerFileName = ".wheelwizard-work";

    public string RootPath => fileSystem.Path.Combine(applicationData.DirectoryPath, FolderName);
    public string DataPath => fileSystem.Path.Combine(RootPath, "DATA");
    public string MainDolPath => fileSystem.Path.Combine(DataPath, "sys", "main.dol");
    public string StaticRelPath => fileSystem.Path.Combine(DataPath, "files", "rel", "StaticR.rel");
    public string BootBinPath => fileSystem.Path.Combine(DataPath, "sys", "boot.bin");
    public string ManifestPath => fileSystem.Path.Combine(RootPath, ManifestFileName);

    public bool IsLinked => LinkTarget() is not null;

    public string ResolvedRootPath => LinkTarget() ?? RootPath;

    public string StagingPath => ResolvedRootPath + ".partial";
    public string ReplacedPath => ResolvedRootPath + ".replaced";

    private string? LinkTarget()
    {
        try
        {
            var directory = fileSystem.DirectoryInfo.New(RootPath);
            if (directory.LinkTarget is null)
                return null;
            var target = directory.ResolveLinkTarget(returnFinalTarget: true);
            return target is null ? null : fileSystem.Path.GetFullPath(target.FullName);
        }
        catch
        {
            return null;
        }
    }
}
