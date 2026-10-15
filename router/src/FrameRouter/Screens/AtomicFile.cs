namespace FrameRouter.Screens;

internal static class AtomicFile
{
    // Write then rename: a crash mid-write must not leave a half file for the next start.
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }
}
