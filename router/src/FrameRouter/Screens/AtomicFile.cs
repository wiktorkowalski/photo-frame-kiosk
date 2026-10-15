namespace FrameRouter.Screens;

internal static class AtomicFile
{
    // Write then rename: a crash mid-write must not leave a half file for the next start.
    public static async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }
}
