using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Shared;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.WiiManagement.GameExtraction;

public class GameFileExtractionService : IGameFileExtractionService
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<GameFileExtractionService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public GameFileExtractionService(IFileSystem fileSystem, ILogger<GameFileExtractionService> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public string GetExtractionRootPath() => PathManager.GameExtractionRootFolderPath;

    public string GetLaunchFilePath()
    {
        var rootPath = GetExtractionRootPath();
        var metadata = LoadMetadata();
        return ResolveMainDolPath(metadata, rootPath) ?? PathManager.ExtractedMainDolFilePath;
    }

    public async Task<OperationResult<string>> EnsureExtractedAsync(CancellationToken cancellationToken = default)
    {
        var sourcePath = PathManager.GameFilePath;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return new OperationError { Message = "Game file path is not configured." };
        }

        sourcePath = NormalizePath(sourcePath);
        if (!_fileSystem.File.Exists(sourcePath))
        {
            return new OperationError { Message = $"The configured game file could not be found at '{sourcePath}'." };
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var destinationRoot = GetExtractionRootPath();
            var metadata = LoadMetadata();
            var sourceInfo = _fileSystem.FileInfo.New(sourcePath);

            if (IsExtractionUpToDate(metadata, sourceInfo, sourcePath, destinationRoot))
            {
                var cachedMainDol = ResolveMainDolPath(metadata, destinationRoot);
                if (!string.IsNullOrWhiteSpace(cachedMainDol))
                    return OperationResult.Ok(cachedMainDol);
            }

            return await ExtractAsync(sourcePath, destinationRoot, sourceInfo, cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<OperationResult<string>> ExtractAsync(
        string sourcePath,
        string destinationRoot,
        IFileInfo sourceInfo,
        CancellationToken cancellationToken
    )
    {
        ProgressWindow? progressWindow = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            progressWindow = new ProgressWindow(Common.State_Extracting)
                .SetGoal($"{Common.State_Extracting} {sourceInfo.Name}")
                .SetExtraText(Phrases.Progress_ThisMayTakeAWhile)
                .SetIndeterminate(true);
            progressWindow.Show();
        });

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => progressWindow?.UpdateProgress(5));

            if (_fileSystem.Directory.Exists(destinationRoot))
            {
                _fileSystem.Directory.Delete(destinationRoot, recursive: true);
            }

            var parentDirectory = _fileSystem.Path.GetDirectoryName(destinationRoot);
            if (!string.IsNullOrWhiteSpace(parentDirectory) && !_fileSystem.Directory.Exists(parentDirectory))
            {
                _fileSystem.Directory.CreateDirectory(parentDirectory);
            }

            var buildCommandResult = BuildExtractionCommand(sourcePath, destinationRoot);
            if (buildCommandResult.IsFailure)
            {
                return new OperationError { Message = buildCommandResult.Error.Message, Exception = buildCommandResult.Error.Exception };
            }

            var command = buildCommandResult.Value;

            var runResult = await RunProcessAsync(command, cancellationToken);
            if (runResult.IsFailure)
            {
                return runResult.Error ?? new OperationError { Message = "dolphin-tool command failed." };
            }

            var discoveredMainDol = FindMainDol(destinationRoot);
            if (string.IsNullOrWhiteSpace(discoveredMainDol))
            {
                return new OperationError { Message = "The extraction completed but the main.dol file was not found in the output." };
            }

            string relativeMainDolPath;
            try
            {
                relativeMainDolPath = _fileSystem.Path.GetRelativePath(destinationRoot, discoveredMainDol);
            }
            catch
            {
                relativeMainDolPath = _fileSystem.Path.GetFullPath(discoveredMainDol);
            }

            var metadata = new GameExtractionMetadata
            {
                SourcePath = sourcePath,
                SourceFileSize = sourceInfo.Length,
                SourceLastWriteTimeUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
                ExtractedAtUtc = DateTime.UtcNow,
                MainDolRelativePath = relativeMainDolPath,
            };
            SaveMetadata(metadata);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                progressWindow?.SetIndeterminate(false);
                progressWindow?.UpdateProgress(100);
            });
            return OperationResult.Ok(discoveredMainDol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract game files");
            return new OperationError { Message = "Failed to extract the game files.", Exception = ex };
        }
        finally
        {
            if (progressWindow != null)
            {
                await Dispatcher.UIThread.InvokeAsync(progressWindow.Close);
            }
        }
    }

    private OperationResult<GameExtractionMetadata> LoadMetadata()
    {
        try
        {
            var metadataPath = PathManager.GameExtractionMetadataFilePath;
            if (!_fileSystem.File.Exists(metadataPath))
                return new OperationError { Message = "Metadata not found" };

            var json = _fileSystem.File.ReadAllText(metadataPath);
            if (string.IsNullOrWhiteSpace(json))
                return new OperationError { Message = "Metadata is empty" };

            var metadata = JsonSerializer.Deserialize<GameExtractionMetadata>(json);
            if (metadata == null)
                return new OperationError { Message = "Failed to deserialize metadata" };

            return OperationResult.Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read extraction metadata.");
            return new OperationError { Message = ex.Message, Exception = ex };
        }
    }

    private void SaveMetadata(GameExtractionMetadata metadata)
    {
        var metadataPath = PathManager.GameExtractionMetadataFilePath;
        var directory = _fileSystem.Path.GetDirectoryName(metadataPath);
        if (!string.IsNullOrWhiteSpace(directory) && !_fileSystem.Directory.Exists(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        _fileSystem.File.WriteAllText(metadataPath, json);
    }

    private bool IsExtractionUpToDate(
        OperationResult<GameExtractionMetadata> metadataResult,
        IFileInfo sourceInfo,
        string sourcePath,
        string destinationRoot
    )
    {
        if (!metadataResult.IsSuccess)
            return false;

        var metadata = metadataResult.Value;
        if (string.IsNullOrWhiteSpace(metadata.MainDolRelativePath))
            return false;

        string mainDolCandidate;
        try
        {
            mainDolCandidate = _fileSystem.Path.IsPathRooted(metadata.MainDolRelativePath)
                ? metadata.MainDolRelativePath
                : _fileSystem.Path.Combine(destinationRoot, metadata.MainDolRelativePath);
        }
        catch
        {
            return false;
        }

        if (!_fileSystem.File.Exists(mainDolCandidate))
            return false;

        return string.Equals(metadata.SourcePath, sourcePath, StringComparison.Ordinal)
            && metadata.SourceFileSize == sourceInfo.Length
            && metadata.SourceLastWriteTimeUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks;
    }

    private string? ResolveMainDolPath(OperationResult<GameExtractionMetadata> metadataResult, string rootPath)
    {
        if (metadataResult.IsSuccess)
        {
            var metadata = metadataResult.Value;
            if (!string.IsNullOrWhiteSpace(metadata.MainDolRelativePath))
            {
                try
                {
                    var candidate = _fileSystem.Path.IsPathRooted(metadata.MainDolRelativePath)
                        ? metadata.MainDolRelativePath
                        : _fileSystem.Path.Combine(rootPath, metadata.MainDolRelativePath);
                    if (_fileSystem.File.Exists(candidate))
                        return candidate;
                }
                catch
                {
                    // Ignore invalid paths
                }
            }
        }

        var fallbackCandidates = new[]
        {
            _fileSystem.Path.Combine(rootPath, "DATA", "sys", "main.dol"),
            _fileSystem.Path.Combine(rootPath, "sys", "main.dol"),
            PathManager.ExtractedMainDolFilePath,
        };

        foreach (var candidate in fallbackCandidates)
        {
            if (_fileSystem.File.Exists(candidate))
                return candidate;
        }

        return FindMainDol(rootPath);
    }

    private string? FindMainDol(string rootPath)
    {
        try
        {
            if (!_fileSystem.Directory.Exists(rootPath))
                return null;

            var candidates = _fileSystem.Directory.EnumerateFiles(rootPath, "main.dol", SearchOption.AllDirectories).ToList();

            if (candidates.Count == 0)
                return null;

            string? Prefer(string segment)
            {
                return candidates.FirstOrDefault(path => path.Contains(segment, StringComparison.OrdinalIgnoreCase));
            }

            var preferred =
                Prefer($"{Path.DirectorySeparatorChar}DATA{Path.DirectorySeparatorChar}sys{Path.DirectorySeparatorChar}main.dol")
                ?? Prefer($"{Path.DirectorySeparatorChar}DATA{Path.DirectorySeparatorChar}");

            return preferred ?? candidates[0];
        }
        catch
        {
            return null;
        }
    }

    private OperationResult<DolphinToolCommand> BuildExtractionCommand(string sourcePath, string destinationRoot)
    {
        var absoluteSource = _fileSystem.Path.GetFullPath(sourcePath);
        var absoluteDestination = _fileSystem.Path.GetFullPath(destinationRoot);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var dolphinLocation = NormalizePath(PathManager.DolphinFilePath);
            var directory = _fileSystem.Path.GetDirectoryName(dolphinLocation);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return new OperationError { Message = "Unable to determine Dolphin install directory." };
            }

            var toolPath = _fileSystem.Path.Combine(directory, "DolphinTool.exe");
            if (!_fileSystem.File.Exists(toolPath))
            {
                return new OperationError { Message = "DolphinTool.exe could not be found. Please reinstall or update Dolphin." };
            }

            return OperationResult.Ok(
                new DolphinToolCommand(toolPath, new[] { "extract", "--input", absoluteSource, "--output", absoluteDestination })
            );
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var toolPath = ResolveToolBesideExecutable("dolphin-tool");
            if (toolPath == null)
                toolPath = ResolveToolBesideExecutable("DolphinTool");

            if (toolPath == null)
            {
                if (EnvHelper.IsValidUnixCommand("dolphin-tool"))
                {
                    return OperationResult.Ok(
                        new DolphinToolCommand(
                            "/usr/bin/env",
                            new[] { "dolphin-tool", "extract", "--input", absoluteSource, "--output", absoluteDestination }
                        )
                    );
                }

                return new OperationError { Message = "Unable to locate dolphin-tool. Please ensure Dolphin is installed correctly." };
            }

            return OperationResult.Ok(
                new DolphinToolCommand(toolPath, new[] { "extract", "--input", absoluteSource, "--output", absoluteDestination })
            );
        }

        // Linux
        if (PathManager.IsFlatpakDolphinFilePath())
        {
            return OperationResult.Ok(
                new DolphinToolCommand(
                    "/usr/bin/env",
                    new[]
                    {
                        "flatpak",
                        "run",
                        "--command=dolphin-tool",
                        "org.DolphinEmu.dolphin-emu",
                        "extract",
                        "--input",
                        absoluteSource,
                        "--output",
                        absoluteDestination,
                    }
                )
            );
        }

        var nativeTool = ResolveToolBesideExecutable("dolphin-tool") ?? "dolphin-tool";

        if (nativeTool == "dolphin-tool" && !EnvHelper.IsValidUnixCommand(nativeTool))
        {
            return new OperationError { Message = "dolphin-tool could not be found in PATH." };
        }

        if (nativeTool == "dolphin-tool")
        {
            return OperationResult.Ok(
                new DolphinToolCommand(
                    "/usr/bin/env",
                    new[] { "dolphin-tool", "extract", "--input", absoluteSource, "--output", absoluteDestination }
                )
            );
        }

        return OperationResult.Ok(
            new DolphinToolCommand(nativeTool, new[] { "extract", "--input", absoluteSource, "--output", absoluteDestination })
        );
    }

    private string? ResolveToolBesideExecutable(string executableName)
    {
        var dolphinLocation = NormalizePath(PathManager.DolphinFilePath);
        if (!_fileSystem.File.Exists(dolphinLocation))
            return null;

        var directory = _fileSystem.Path.GetDirectoryName(dolphinLocation);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        var candidate = _fileSystem.Path.Combine(directory, executableName);
        if (_fileSystem.File.Exists(candidate))
            return candidate;

        return null;
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim();
        if (
            trimmed.Length > 1
            && ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || (trimmed.StartsWith("'") && trimmed.EndsWith("'")))
        )
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
    }

    private async Task<OperationResult> RunProcessAsync(DolphinToolCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argument in command.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (command.EnvironmentVariables != null)
            {
                foreach (var kvp in command.EnvironmentVariables)
                {
                    startInfo.Environment[kvp.Key] = kvp.Value;
                }
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new OperationError { Message = "Failed to start dolphin-tool process." };
            }

            var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await stdOutTask;
            var errorOutput = await stdErrTask;

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(errorOutput) ? output : errorOutput;
                return new OperationError
                {
                    Message = $"dolphin-tool failed with exit code {process.ExitCode}.",
                    ExtraReplacements = [message],
                };
            }

            if (!string.IsNullOrWhiteSpace(output))
                _logger.LogDebug("dolphin-tool output: {Output}", output);
            if (!string.IsNullOrWhiteSpace(errorOutput))
                _logger.LogDebug("dolphin-tool warnings: {Output}", errorOutput);

            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "dolphin-tool execution failed");
            return new OperationError { Message = "Failed to execute dolphin-tool.", Exception = ex };
        }
    }

    private sealed record DolphinToolCommand(
        string FileName,
        IReadOnlyList<string> Arguments,
        IReadOnlyDictionary<string, string>? EnvironmentVariables = null
    );
}
