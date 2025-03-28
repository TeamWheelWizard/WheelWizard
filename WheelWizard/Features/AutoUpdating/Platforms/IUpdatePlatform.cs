﻿using WheelWizard.Models.Github;

namespace WheelWizard.AutoUpdating.Platforms;

/// <summary>
/// Interface for platform-specific update logic.
/// </summary>
public interface IUpdatePlatform
{
    /// <summary>
    /// Gets the asset for the current platform.
    /// </summary>
    GithubAsset? GetAssetForCurrentPlatform(GithubRelease release);
    
    /// <summary>
    /// Executes the update logic for the current platform.
    /// </summary>
    Task ExecuteUpdateAsync(string downloadUrl);
}
