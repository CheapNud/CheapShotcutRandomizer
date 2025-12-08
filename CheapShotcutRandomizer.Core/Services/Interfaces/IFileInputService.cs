namespace CheapShotcutRandomizer.Core.Services.Interfaces;

/// <summary>
/// File input abstraction for different platforms
/// Desktop: Native file picker dialogs (Avalonia)
/// Server: Path input, browser upload, or API-driven
/// </summary>
public interface IFileInputService
{
    /// <summary>
    /// Open file picker dialog (desktop) or return null (server - use path input instead)
    /// </summary>
    Task<FileInputResult?> PickFileAsync(FilePickerOptions options);

    /// <summary>
    /// Open folder picker dialog (desktop) or return null (server - use path input instead)
    /// </summary>
    Task<string?> PickFolderAsync(string? title = null);

    /// <summary>
    /// Save file dialog (desktop) or save to output directory (server)
    /// </summary>
    Task<string?> SaveFileAsync(string suggestedName, string? defaultExtension = null);

    /// <summary>
    /// Check if a path exists and is accessible
    /// </summary>
    Task<bool> PathExistsAsync(string path);

    /// <summary>
    /// Check if a path is a file
    /// </summary>
    Task<bool> IsFileAsync(string path);

    /// <summary>
    /// Check if a path is a directory
    /// </summary>
    Task<bool> IsDirectoryAsync(string path);

    /// <summary>
    /// Get files from a directory path
    /// </summary>
    Task<IReadOnlyList<string>> GetFilesInDirectoryAsync(string path, string? searchPattern = null, bool recursive = false);

    /// <summary>
    /// True if native file picker is supported on this platform
    /// </summary>
    bool SupportsNativeFilePicker { get; }
}

/// <summary>
/// Options for file picker dialog
/// </summary>
public record FilePickerOptions(
    string? Title = null,
    IReadOnlyList<FilePickerFilter>? Filters = null,
    bool AllowMultiple = false,
    string? InitialDirectory = null);

/// <summary>
/// File type filter for picker dialog
/// </summary>
public record FilePickerFilter(
    string Name,
    IReadOnlyList<string> Extensions);

/// <summary>
/// Result from file picker
/// </summary>
public record FileInputResult(
    string FileName,
    string FullPath,
    long? FileSizeBytes = null);
