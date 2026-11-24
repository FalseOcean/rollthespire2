namespace RolltheSpire2.Ui.Persistence;

internal static class SearchPersistenceFile
{
    // Only the committed destination is loadable. An interrupted temporary file is
    // never promoted on read; its contents may not represent a completed user action.
    internal static void WriteAtomic(string path, byte[] payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            // Preserve the original write exception, and only clean our own temp.
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
