using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

class Program
{
    const long RequiredFree = 150L * 1024L * 1024L; // ~150 MB нужни за разопакуване

    static long GetFreeBytes(string path)
    {
        try
        {
            var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)));
            return d.IsReady ? d.AvailableFreeSpace : -1;
        }
        catch { return -1; }
    }

    static string PickTempRoot(string exeDir)
    {
        // пътя на екзето е на ПРЪВО място (диска на проекта), Temp е резервна опция
        var cands = new List<string> { exeDir, Path.GetTempPath() };

        foreach (string baseDir in cands)
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;
            long free = GetFreeBytes(baseDir);
            if (free < 0 || free < RequiredFree) continue;

            string dir = Path.Combine(baseDir,
                "VideoCompressor_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { Directory.CreateDirectory(dir); } catch { continue; }
            return dir;
        }
        return null;
    }

    // почиствам стари "зависнали" временни папки от предишни крашове
    static void CleanupStaleDirs(string exeDir)
    {
        var bases = new List<string> { exeDir, Path.GetTempPath() };
        foreach (string baseDir in bases)
        {
            if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;
            try
            {
                foreach (string d in Directory.GetDirectories(baseDir, "VideoCompressor_*"))
                {
                    try
                    {
                        if ((DateTime.Now - Directory.GetLastWriteTime(d)).TotalMinutes > 5)
                            Directory.Delete(d, true);
                    }
                    catch { }
                }
            }
            catch { }
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        CleanupStaleDirs(exeDir);

        string root = PickTempRoot(exeDir);
        if (root == null)
        {
            MessageBox.Show(
                "Няма достатъчно място на нито един диск!\n\n" +
                "Освободи поне 150 MB на диска с exe-то или на C:\\.",
                "Video Compressor 1.0");
            return 1;
        }

        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("payload.zip"))
            {
                if (s == null) throw new Exception("Вграден файл payload.zip липсва!");
                using (var zip = new ZipArchive(s))
                {
                    zip.ExtractToDirectory(root);
                }
            }

            string ps1 = Path.Combine(root, "VideoCompressor.ps1");
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + ps1 + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string a in args)
                psi.Arguments += " \"" + a + "\"";

            using (Process p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Video Compressor 1.0");
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
