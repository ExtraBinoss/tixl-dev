#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using System.Linq; // Added for Any()
using T3.Core.UserData;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static partial class RenderPaths
{
    private static readonly Regex _matchFileVersionPattern = FileVersionPatternRegex();

    public static string ResolveProjectRelativePath(string path)
    {
        var project = ProjectView.Focused?.OpenedProject;
        if (project != null && path.StartsWith('.'))
        {
            return Path.Combine(project.Package.Folder, path);
        }

        // TODO: Make project directory selection smarter
        return path.StartsWith('.')
                   ? Path.Combine(UserSettings.Config.ProjectDirectories[0], FileLocations.RenderSubFolder, path)
                   : path;
    }

    public static string GetTargetFilePath(FFMpegRenderSettings.RenderModes mode)
    {
        if (mode == FFMpegRenderSettings.RenderModes.Video)
        {
            return ResolveProjectRelativePath(UserSettings.Config.RenderVideoFilePath ?? string.Empty);
        }

        var folder = ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath ?? string.Empty);
        var subFolder = UserSettings.Config.RenderSequenceFileName ?? "v01";
        var prefix = UserSettings.Config.RenderSequencePrefix ?? "render";

        if (FFMpegRenderSettings.Current.CreateSubFolder)
        {
            return Path.Combine(folder, subFolder, prefix);
        }

        return Path.Combine(folder, prefix);
    }

    public static string GetExpectedTargetDisplayPath(FFMpegRenderSettings.RenderModes mode)
    {
        var targetPath = GetTargetFilePath(mode);
        var settings = FFMpegRenderSettings.Current;

        if (mode == FFMpegRenderSettings.RenderModes.Video)
        {
            // Note: FFMpegRenderSettings doesn't have AutoIncrementVersionNumber boolean explicitly, 
            // but the filename itself implies it if "v01" is present.
            // Assuming users want to see the NEXT path if it exists.
            if (IsFilenameIncrementable(targetPath) && File.Exists(targetPath))
            {
               return GetNextIncrementedPath(targetPath);
            }
            return targetPath;
        }

        // Image sequence
        var subFolder = UserSettings.Config.RenderSequenceFileName ?? "v01";
        var prefix = UserSettings.Config.RenderSequencePrefix ?? "render";
        
        if (settings.AutoIncrementSubFolder)
        {
            var folder = ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath ?? string.Empty);
            
            if (settings.CreateSubFolder)
            {
                var nextFolderPath = GetNextVersionForFolder(folder, subFolder);
                subFolder = Path.GetFileName(nextFolderPath);
            }
            else
            {
               // Prefix increment logic
               var targetToIncrement = prefix;
               if (!IsFilenameIncrementable(targetToIncrement) || FileExists(Path.Combine(folder, prefix), mode))
               {
                   var testPath = Path.Combine(folder, prefix);
                   if (IsFilenameIncrementable(prefix))
                   {
                   }
               }
            }
        }

        var baseFolder = ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath ?? string.Empty);
        var finalBase = settings.CreateSubFolder ? Path.Combine(baseFolder, subFolder, prefix) : Path.Combine(baseFolder, prefix);
        
        return $"{finalBase}_%04d.{settings.FileFormat.ToString().ToLower()}";
    }

    public static bool FileExists(string targetPath, FFMpegRenderSettings.RenderModes mode)
    {
        if (mode == FFMpegRenderSettings.RenderModes.Video)
        {
            return File.Exists(targetPath);
        }

        // For image sequences, check if the first frame or the folder exists
        if (FFMpegRenderSettings.Current.CreateSubFolder)
        {
            var directory = Path.GetDirectoryName(targetPath);
            
            if (directory != null && Directory.Exists(directory))
            {
                try
                {
                    return Directory.EnumerateFileSystemEntries(directory).Any();
                }
                catch
                {
                    return true; // Assume exists if we can't access
                }
            }
            return false;
        }

        var firstFrame = $"{targetPath}0000.{FFMpegRenderSettings.Current.FileFormat.ToString().ToLower()}";
        return File.Exists(firstFrame) || File.Exists(firstFrame.Replace("0000", "0001"));
    }

    public static bool ValidateOrCreateTargetFolder(string targetFile)
    {
        var directory = Path.GetDirectoryName(targetFile);
        if (directory == null || Directory.Exists(directory))
            return true;

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to create target folder '{directory}': {e.Message}");
            return false;
        }

        return true;
    }

    public static string SanitizeFilename(string filename)
    {
        if (string.IsNullOrEmpty(filename))
            return "output";

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
            filename = filename.Replace(c.ToString(), "_");

        return filename.Trim();
    }

    public static bool IsFilenameIncrementable(string? path = null)
    {
        var filename = Path.GetFileName(path ?? UserSettings.Config.RenderVideoFilePath);
        return !string.IsNullOrEmpty(filename) && _matchFileVersionPattern.Match(filename).Success;
    }

    public static void TryIncrementVideoFileNameInUserSettings()
    {
        if (FFMpegRenderSettings.Current.RenderMode == FFMpegRenderSettings.RenderModes.ImageSequence)
        {
             // Increment sequence settings
             var sub = UserSettings.Config.RenderSequenceFileName;
             if (IsFilenameIncrementable(sub))
             {
                 UserSettings.Config.RenderSequenceFileName = GetNextIncrementedPath(sub!);
             }
             UserSettings.Save();
             return;
        }
    
        var filename = Path.GetFileName(UserSettings.Config.RenderVideoFilePath);
        if (string.IsNullOrEmpty(filename))
            return;

        if (IsFilenameIncrementable(filename))
        {
            var newFilename = GetNextIncrementedPath(filename);
            var directoryName = Path.GetDirectoryName(UserSettings.Config.RenderVideoFilePath);
            UserSettings.Config.RenderVideoFilePath = directoryName == null
                                                        ? newFilename
                                                        : Path.Combine(directoryName, newFilename);
            UserSettings.Save();
        }
    }

    public static string GetNextIncrementedPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "output_v01"; // Fallback to v01 if empty

        var filename = Path.GetFileName(path);
        // If path is just filename, DirectoryName is null/empty.
        var directory = Path.GetDirectoryName(path);
        string newFilename;

        var match = _matchFileVersionPattern.Match(filename);
        if (!match.Success)
        {
            newFilename = filename + "_v01";
        }
        else
        {
            var versionGroup = match.Groups[1];
            var versionString = versionGroup.Value;
            
            if (!int.TryParse(versionString, out var versionNumber))
            {
                newFilename = filename + "_v01";
            }
            else
            {
                var digits = Math.Clamp(versionString.Length, 2, 4);
                // Ensure we don't overflow logic if 9999?
                var newVersionNumberString = (versionNumber + 1).ToString("D" + digits);
                
                // Replace only the version number part within the matched group
                newFilename = filename.Remove(versionGroup.Index, versionGroup.Length)
                                      .Insert(versionGroup.Index, newVersionNumberString);
            }
        }
        
        if (string.IsNullOrEmpty(directory)) return newFilename;
        return Path.Combine(directory, newFilename);
    }

    [GeneratedRegex(@"(?:^|[\s_\-.])v(\d{2,4})(?:\b|$)")]
    private static partial Regex FileVersionPatternRegex();

    public static string GetNextVersionForFolder(string mainFolder, string subFolder)
    {
        var result = _matchFileVersionPattern.Match(subFolder);
        if (!result.Success)
        {
            var newSub = subFolder + "_v01";
            var path = Path.Combine(mainFolder, newSub);
            if (Directory.Exists(path))
            {
                return GetNextVersionForFolder(mainFolder, newSub);
            }
            return path;
        }

        var versionString = result.Groups[1].Value;
        if (!int.TryParse(versionString, out var versionNumber))
            return Path.Combine(mainFolder, subFolder);

        var digits = Math.Clamp(versionString.Length, 2, 4);
        
        for (int i = 0; i < 1000; i++)
        {
             var fullPath = Path.Combine(mainFolder, subFolder);
             if (!Directory.Exists(fullPath))
                 return fullPath;

             versionNumber++;
             var newVersionString = "v" + versionNumber.ToString("D" + digits);
             subFolder = _matchFileVersionPattern.Replace(subFolder, newVersionString, 1);
        }
        
        return Path.Combine(mainFolder, subFolder);
    }
}