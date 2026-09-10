using System;
using System.IO;
using System.Text;

/// <summary>
/// 关键持久化文件的原子写入工具。
/// 写入完成前保留旧文件；替换时留下最近一次有效版本的 .bak 文件，
/// 避免崩溃或断电把正式 JSON 截断为空文件。
/// </summary>
public static class AtomicFileWriter
{
    /// <summary>将文本完整写入临时文件后原子替换目标文件。</summary>
    public static void WriteAllText(string path, string content, Encoding encoding)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("目标路径不能为空", nameof(path));
        if (encoding == null) throw new ArgumentNullException(nameof(encoding));

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string backupPath = path + ".bak";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, encoding))
            {
                writer.Write(content ?? string.Empty);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(path))
                File.Replace(tempPath, path, backupPath, true);
            else
                File.Move(tempPath, path);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
