using Microsoft.Extensions.Options;
using Salvarr.Arr;
using Salvarr.Options;

namespace Salvarr.Library;

public sealed record ActionResult(bool Success, string Message);

/// <summary>
/// Restore and delete. Every action is refused unless AllowChanges is on, and
/// only works on files the latest scan found in a recycle bin.
/// </summary>
public sealed class FileActions(IOptions<SalvarrOptions> options, ArrService arr, ILogger<FileActions> logger)
{
    public bool Enabled => options.Value.AllowChanges;

    /// <summary>Why a restore cannot happen, or null when it can.</summary>
    public static string? RestoreBlocker(RecycledFile file)
    {
        if (file.Match.OriginalPath is not { } target)
        {
            return "Salvarr does not know where this file was.";
        }

        return File.Exists(target) ? "A file with the same name is already there." : null;
    }

    /// <summary>
    /// Moves the file (and its subtitles or NFO with the same name) back to
    /// where it was, then asks Radarr or Sonarr to rescan the movie or show.
    /// </summary>
    public async Task<ActionResult> RestoreAsync(RecycledFile file, bool rescan)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        if (RestoreBlocker(file) is { } blocker)
        {
            return new ActionResult(false, blocker);
        }

        var target = file.Match.OriginalPath!;
        var targetFolder = Path.GetDirectoryName(target)!;
        try
        {
            Directory.CreateDirectory(targetFolder);
            var companions = Companions(file.FullPath).ToList();
            File.Move(file.FullPath, target);
            logger.LogInformation("Restored {Source} to {Target}", file.FullPath, target);

            foreach (var companion in companions)
            {
                var destination = Path.Combine(targetFolder, Path.GetFileName(companion));
                if (!File.Exists(destination))
                {
                    File.Move(companion, destination);
                    logger.LogInformation("Restored {Source} to {Target}", companion, destination);
                }
            }

            RemoveEmptyFolders(Path.GetDirectoryName(file.FullPath)!, file.Bin.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Restore failed for {Path}", file.FullPath);
            return new ActionResult(false, $"Could not move the file: {e.Message}{ReadOnlyHint(e)}");
        }

        var message = $"Moved back to {target}.";
        if (rescan)
        {
            message += " " + await arr.RescanAsync(file.Bin.Library, file.Match.TitleFolder ?? targetFolder);
        }

        return new ActionResult(true, message);
    }

    /// <summary>Deletes the file for good, with its subtitles or NFO of the same name.</summary>
    public ActionResult Delete(RecycledFile file)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        try
        {
            DeleteWithCompanions(file);
            RemoveEmptyFolders(Path.GetDirectoryName(file.FullPath)!, file.Bin.Path);
            return new ActionResult(true, $"Deleted {file.FileName}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Delete failed for {Path}", file.FullPath);
            return new ActionResult(false, $"Could not delete the file: {e.Message}{ReadOnlyHint(e)}");
        }
    }

    /// <summary>Deletes every file in <paramref name="files"/>. Stops at the first error.</summary>
    public ActionResult DeleteMany(IReadOnlyCollection<RecycledFile> files)
    {
        if (!Enabled)
        {
            return Disabled();
        }

        var deleted = 0;
        long bytes = 0;
        foreach (var file in files)
        {
            try
            {
                DeleteWithCompanions(file);
                RemoveEmptyFolders(Path.GetDirectoryName(file.FullPath)!, file.Bin.Path);
                deleted++;
                bytes += file.SizeBytes;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Delete failed for {Path}", file.FullPath);
                return new ActionResult(false,
                    $"Deleted {deleted} of {files.Count} files, then failed on {file.FileName}: {e.Message}{ReadOnlyHint(e)}");
            }
        }

        return new ActionResult(true, $"Deleted {deleted} {(deleted == 1 ? "file" : "files")} ({Media.Format.Size(bytes)}).");
    }

    /// <summary>Files in the same folder that belong to a video: "Name.en.srt", "Name.nfo" for "Name.mkv".</summary>
    public static IEnumerable<string> Companions(string videoPath)
    {
        var folder = Path.GetDirectoryName(videoPath)!;
        var prefix = Path.GetFileNameWithoutExtension(videoPath) + ".";
        return Directory.EnumerateFiles(folder)
            .Where(f => f != videoPath && Path.GetFileName(f).StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>Removes empty folders from <paramref name="folder"/> up to, but not including, the bin itself.</summary>
    public static void RemoveEmptyFolders(string folder, string binPath)
    {
        var bin = Path.TrimEndingDirectorySeparator(binPath);
        var current = Path.TrimEndingDirectorySeparator(folder);
        while (current.Length > bin.Length
               && current.StartsWith(bin + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && Directory.Exists(current)
               && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current)!;
        }
    }

    private void DeleteWithCompanions(RecycledFile file)
    {
        foreach (var companion in Companions(file.FullPath))
        {
            File.Delete(companion);
            logger.LogInformation("Deleted {Path}", companion);
        }

        File.Delete(file.FullPath);
        logger.LogInformation("Deleted {Path}", file.FullPath);
    }

    private static ActionResult Disabled()
    {
        return new ActionResult(false, "Changes are turned off. Set Salvarr__AllowChanges=true and mount the libraries read-write.");
    }

    private static string ReadOnlyHint(Exception e)
    {
        return e.Message.Contains("Read-only", StringComparison.OrdinalIgnoreCase)
            ? " The library is mounted read-only: remove :ro from the volume."
            : "";
    }
}
