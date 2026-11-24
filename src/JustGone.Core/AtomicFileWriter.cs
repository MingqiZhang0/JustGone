using System;
using System.IO;
using System.Text;

namespace JustGone.Core
{
    /// <summary>
    /// Atomic UTF-8 file writer using exclusive temp file + Flush(true) + File.Replace/Move.
    /// Directly reused from WeiXiangQi v0.10.10.0 (shared_p4/WxqAtomicConfigFile.cs, verified under WXQ-P1-001).
    /// </summary>
    public static class AtomicFileWriter
    {
        public static void WriteAllTextAtomic(string path, string content, string backupPath = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("File path is required.", nameof(path));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bool created = false;
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content ?? string.Empty);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporary, path, backupPath);
                else
                    File.Move(temporary, path);
            }
            catch
            {
                if (created)
                {
                    try { File.Delete(temporary); } catch { }
                }
                throw;
            }
        }
    }
}
