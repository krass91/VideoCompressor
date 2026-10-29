// ============================================================
//  Video Compressor 1.2 — портабъл, без PowerShell
//  Временните файлове отиват на диск с МЯСТО (не на пълния C:),
//  подробни грешки на екрана + лог файл до exe-то.
// ============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class VideoCompressor
{
    static readonly string[] VideoExts = {
        ".mp4",".mkv",".avi",".mov",".wmv",".flv",".webm",".m4v",".mpg",".mpeg",".ts",".m2ts",".mts",
        ".3gp",".3g2",".ogv",".vob",".rm",".rmvb",".asf",".divx",".f4v",".mxf",".wtv",".dvr-ms",".amv",
        ".m1v",".m2v",".dv",".mpe",".qt",".264",".265",".h264",".h265",".hevc",".m2p",".tp",".trp",".ismv"
    };
    const long RequiredFree = 150L * 1024L * 1024L; // ~150 MB

    static ListBox fileList;
    static Label status;
    static TrackBar trackQ;
    static ComboBox comboPreset, comboScale;
    static CheckBox chkCopy;
    static ProgressBar progress;
    static Button btnGo;
    static Form form;
    static string workDir;      // временна работна папка на диск с място
    static string fallbackDir;  // резерва: пази резултати ако целевият диск е пълен
    static string LogFile;
    static volatile string ffmpegTimeLine;

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();

        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        LogFile = Path.Combine(exeDir, "VideoCompressor-log.txt");
        Log("========== Старт Video Compressor 1.2 ==========");
        Log("exeDir=" + exeDir);

        CleanupStaleDirs(exeDir);
        workDir = PickRoot(exeDir);
        if (workDir == null)
        {
            MessageBox.Show("Няма място на нито един диск!\nОсвободи поне 150 MB на диска с exe-то.",
                "Video Compressor 1.2");
            Log("ГРЕШКА: няма диск с 150 MB свободни");
            return 1;
        }
        Log("workDir=" + workDir);

        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (s == null) throw new Exception("Вграден пакет липсва!");
                using (var zip = new ZipArchive(s)) zip.ExtractToDirectory(workDir);
            }

            string ffmpegProbe = Path.Combine(workDir, "ffmpeg.exe");
            Log("ffmpeg наличие: " + (File.Exists(ffmpegProbe) ? "OK" : "ЛИПСВА!") +
                (File.Exists(ffmpegProbe) ? " (" + new FileInfo(ffmpegProbe).Length + " байта)" : ""));
            if (!File.Exists(ffmpegProbe)) throw new Exception("ffmpeg.exe не се разопакова!");

            BuildUi();
            foreach (string a in args) AddVideoFiles(new[] { a });
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            Log("ГРЕШКА при старте: " + ex);
            MessageBox.Show(ex.Message, "Video Compressor 1.2");
            return 1;
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { }
        }
    }

    static void Log(string s)
    {
        try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine); }
        catch { }
    }

    static long GetFreeBytes(string path)
    {
        try { var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))); return d.IsReady ? d.AvailableFreeSpace : -1; }
        catch { return -1; }
    }

    static string PickRoot(string exeDir)
    {
        var cands = new List<string> { exeDir, Path.GetTempPath() };
        foreach (string b in cands)
        {
            if (string.IsNullOrEmpty(b) || !Directory.Exists(b)) continue;
            if (GetFreeBytes(b) < RequiredFree) { Log("Пълен/недостъпен: " + b); continue; }
            string dir = Path.Combine(b, "VideoCompressor_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { Directory.CreateDirectory(dir); return dir; }
            catch { continue; }
        }
        return null;
    }

    static void CleanupStaleDirs(string exeDir)
    {
        var bases = new List<string> { exeDir, Path.GetTempPath() };
        foreach (string b in bases)
        {
            if (string.IsNullOrEmpty(b) || !Directory.Exists(b)) continue;
            try
            {
                foreach (string d in Directory.GetDirectories(b, "VideoCompressor_*"))
                {
                    try { if ((DateTime.Now - Directory.GetLastWriteTime(d)).TotalMinutes > 5) Directory.Delete(d, true); }
                    catch { }
                }
            }
            catch { }
        }
    }

    static void Add(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); form.Controls.Add(c); }

    static void BuildUi()
    {
        form = new Form
        {
            Text = "Video Compressor 1.2 — компресирай видеата си",
            Size = new Size(780, 660),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            AllowDrop = true,
            Font = new Font("Segoe UI", 10)
        };

        var lbl = new Label { Text = "Пусни видео файлове тук (или бутоните) и натисни КОМПРЕСИРАЙ." };
        Add(lbl, 15, 12, 730, 22);

        var lbllist = new Label { Text = "Файлове за компресиране:" };
        Add(lbllist, 15, 40, 300, 20);

        fileList = new ListBox { SelectionMode = SelectionMode.MultiExtended };
        Add(fileList, 15, 62, 560, 160);

        var btnAdd = new Button { Text = "Добави файлове..." };
        Add(btnAdd, 590, 62, 155, 32);
        btnAdd.Click += (s, e) =>
        {
            using (var d = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Избери видео файлове",
                Filter = "Видео файлове|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.3gp;*.ogv;*.vob;*.m2ts|Всички файлове|*.*"
            })
                if (d.ShowDialog() == DialogResult.OK) AddVideoFiles(d.FileNames);
        };

        var btnFolder = new Button { Text = "Добави папка..." };
        Add(btnFolder, 590, 100, 155, 32);
        btnFolder.Click += (s, e) =>
        {
            using (var d = new FolderBrowserDialog { Description = "Избери папка с видеа (и под-папките)" })
                if (d.ShowDialog() == DialogResult.OK) AddVideoFiles(new[] { d.SelectedPath });
        };

        var btnClear = new Button { Text = "Изчисти листа" };
        Add(btnClear, 590, 138, 155, 32);
        btnClear.Click += (s, e) => { fileList.Items.Clear(); SetStatus("Листът е изчистен."); };

        var lblQ = new Label { Text = "Качество (по-ниско число = по-добра картина, по-голям файл):" };
        Add(lblQ, 15, 240, 560, 20);

        trackQ = new TrackBar { Minimum = 18, Maximum = 40, SmallChange = 1, LargeChange = 1 };
        Add(trackQ, 10, 262, 470, 40);
        trackQ.Value = 26;

        var lblQVal = new Label();
        Add(lblQVal, 495, 270, 250, 22);
        lblQVal.Text = "CRF 26";
        trackQ.ValueChanged += (s, e) => lblQVal.Text = "CRF " + trackQ.Value;

        var lblP = new Label { Text = "Бързост / компресия:" };
        Add(lblP, 15, 316, 200, 20);

        comboPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        Add(comboPreset, 225, 312, 170, 24);
        comboPreset.Items.AddRange(new object[] {
            "Супер бързо (ultrafast)", "Бързо (veryfast)", "Баланс (medium)", "Добра компресия (slow)" });
        comboPreset.SelectedIndex = 1;

        var lblS = new Label { Text = "Максимална резолюция:" };
        Add(lblS, 15, 356, 200, 20);

        comboScale = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        Add(comboScale, 225, 352, 170, 24);
        comboScale.Items.AddRange(new object[] { "ОРИГИНАЛНА", "1080p", "720p", "480p" });
        comboScale.SelectedIndex = 0;

        chkCopy = new CheckBox { Text = "Създай копие (не презаписвай оригинала)", Checked = true };
        Add(chkCopy, 15, 396, 420, 24);

        status = new Label { Text = "Готов." };
        Add(status, 15, 432, 730, 40);

        progress = new ProgressBar();
        Add(progress, 15, 474, 730, 24);

        var lblLog = new Label { Text = "Ако нещо се провали — виж лог файла: VideoCompressor-log.txt до exe-то.",
            ForeColor = Color.DimGray };
        Add(lblLog, 15, 500, 730, 20);

        btnGo = new Button
        {
            Text = "КОМПРЕСИРАЙ!",
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat
        };
        Add(btnGo, 15, 524, 730, 56);
        btnGo.Click += (s, e) => Compress();

        form.DragEnter += (s, e) => e.Effect = DragDropEffects.Copy;
        form.DragDrop += (s, e) => AddVideoFiles((string[])e.Data.GetData(DataFormats.FileDrop));
    }

    static void SetStatus(string t)
    {
        if (status.InvokeRequired) { status.BeginInvoke((Action)(() => status.Text = t)); return; }
        status.Text = t;
    }

    static void AddVideoFiles(string[] paths)
    {
        foreach (string p in paths)
        {
            try
            {
                if (File.Exists(p))
                {
                    if (Array.IndexOf(VideoExts, Path.GetExtension(p).ToLower()) >= 0 && !fileList.Items.Contains(p))
                        fileList.Items.Add(p);
                }
                else if (Directory.Exists(p))
                {
                    foreach (var f in Directory.GetFiles(p, "*.*", SearchOption.AllDirectories))
                        if (Array.IndexOf(VideoExts, Path.GetExtension(f).ToLower()) >= 0 && !fileList.Items.Contains(f))
                            fileList.Items.Add(f);
                }
            }
            catch { }
        }
        SetStatus("В листа: " + fileList.Items.Count + " файл(а).");
    }

    static string Fmt(double mb)
    {
        if (mb >= 1024) return Math.Round(mb / 1024, 2) + " GB";
        return Math.Round(mb, 1) + " MB";
    }

    static string Quote(string s) { return s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s; }

    static void Compress()
    {
        if (fileList.Items.Count == 0)
        {
            MessageBox.Show("Първо добави поне един видео файл!", "Няма файлове");
            return;
        }
        string ffmpeg = Path.Combine(workDir, "ffmpeg.exe");
        if (!File.Exists(ffmpeg)) { MessageBox.Show("ffmpeg.exe липсва в работната папка!"); return; }

        btnGo.Enabled = false;

        // Резервна папка за резултати (на диска на exe-то), ако целевият диск е пълен
        fallbackDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Компресирани видеа");
        try { Directory.CreateDirectory(fallbackDir); } catch { }

        var presets = new Dictionary<string, string> {
            { "Супер бързо (ultrafast)", "ultrafast" }, { "Бързо (veryfast)", "veryfast" },
            { "Баланс (medium)", "medium" }, { "Добра компресия (slow)", "slow" } };
        string preset = presets[(string)comboPreset.SelectedItem];
        int crf = trackQ.Value;
        string scaleSel = (string)comboScale.SelectedItem;
        bool keepCopy = chkCopy.Checked;

        var items = new string[fileList.Items.Count];
        fileList.Items.CopyTo(items, 0);

        string vf = null;
        if (scaleSel == "1080p") vf = "scale=-2:min(1080\\,ih)";
        else if (scaleSel == "720p") vf = "scale=-2:min(720\\,ih)";
        else if (scaleSel == "480p") vf = "scale=-2:min(480\\,ih)";

        int total = items.Length, ok = 0, fail = 0, i = 0;
        long saved = 0;
        progress.Maximum = total;
        progress.Value = 0;

        foreach (string file in items)
        {
            string tmp = null;
            try
            {
                Log("----------- Файл " + (i + 1) + "/" + total + ": " + file);
                string dir = Path.GetDirectoryName(file);
                string name = Path.GetFileNameWithoutExtension(file);
                string ext = Path.GetExtension(file).ToLower();
                bool nativeBox = ext == ".mp4" || ext == ".m4v";
                string outExt = nativeBox ? ext : ".mp4";

                long oldSize = File.Exists(file) ? new FileInfo(file).Length : 0;
                long freeDst = GetFreeBytes(dir);
                Log("очаквана големина=" + oldSize + " байта; свободно на " + dir + " = " + freeDst + " байта");

                // Врем. файл: после при видеото АКо там има място, иначе на диска с място!
                string tmpDir = (freeDst > oldSize + 50L * 1024L * 1024L && freeDst > RequiredFree) ? dir : workDir;
                if (tmpDir != dir)
                    SetStatus("(Целевият диск е пълен — временн. файл отива на " + Path.GetPathRoot(workDir) + ")");

                tmp = Path.Combine(tmpDir, name + "_tmp_" + Guid.NewGuid().ToString("N").Substring(0, 6) + outExt);

                string finalOut;
                if (keepCopy)
                {
                    finalOut = Path.Combine(dir, name + "_compressed" + outExt);
                    if (File.Exists(finalOut))
                        finalOut = Path.Combine(dir, name + "_compressed_" + DateTime.Now.ToString("HHmmss") + outExt);
                }
                else
                {
                    finalOut = Path.Combine(dir, name + outExt);
                }
                Log("tmp=" + tmp + "  finalOut=" + finalOut);

                var args = new List<string> { "-y", "-hide_banner", "-i", file };
                if (vf != null) { args.Add("-vf"); args.Add(vf); }
                args.AddRange(new[] {
                    "-c:v","libx264","-preset",preset,"-crf",crf.ToString(),
                    "-c:a","aac","-b:a","128k","-movflags","+faststart", tmp });

                string cmdline = string.Join(" ", Array.ConvertAll(args.ToArray(), Quote));
                Log("cmd: " + cmdline);

                SetStatus("Компресирам (" + (i + 1) + "/" + total + "): " + Path.GetFileName(file));

                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = cmdline,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = false
                };

                int exitCode;
                var errText = new StringBuilder();
                ffmpegTimeLine = "";
                using (var p = Process.Start(psi))
                {
                    p.ErrorDataReceived += (s2, e2) =>
                    {
                        if (e2.Data == null) return;
                        lock (errText) errText.AppendLine(e2.Data);
                        var m = Regex.Match(e2.Data, @"time=(\S+)");
                        if (m.Success)
                        {
                            ffmpegTimeLine = " време: " + m.Groups[1].Value;
                            SetStatus("Компресирам (" + (i + 1) + "/" + total + "): " +
                                Path.GetFileName(file) + ffmpegTimeLine);
                        }
                    };
                    p.BeginErrorReadLine();
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                }
                Log("ffmpeg exit code: " + exitCode);

                if (exitCode == 0 && File.Exists(tmp))
                {
                    long newSize = new FileInfo(tmp).Length;
                    Log("нов файл: " + newSize + " байта");

                    bool destHasSpace = GetFreeBytes(dir) > newSize + 20L * 1024L * 1024L;
                    string outPath = finalOut;

                    if (keepCopy)
                    {
                        if (destHasSpace) { File.Copy(tmp, finalOut, true); }
                        else { outPath = Path.Combine(fallbackDir, name + "_compressed" + outExt); File.Copy(tmp, outPath, true); }
                    }
                    else
                    {
                        if (newSize < oldSize)
                        {
                            if (destHasSpace)
                            {
                                string bak = file + ".bak";
                                File.Move(file, bak);
                                File.Move(tmp, finalOut);
                                File.Delete(bak);
                            }
                            else
                            {
                                outPath = Path.Combine(fallbackDir, name + "_compressed" + outExt);
                                File.Copy(tmp, outPath, true);
                                File.Delete(tmp);
                            }
                            saved += (oldSize - Math.Min(oldSize, newSize));
                        }
                        else
                        {
                            // оригиналът вече е по-малък — не пипаме нищо
                            outPath = file;
                        }
                    }
                    ok++;
                    SetStatus("✔ " + Path.GetFileName(file) + " → " + Path.GetFileName(outPath));
                    Log("OK: " + outPath);
                }
                else
                {
                    fail++;
                    string tail = errText.ToString().TrimEnd();
                    if (tail.Length > 400) tail = tail.Substring(tail.Length - 400);
                    Log("ГРЕШКА от ffmpeg:\n" + tail);
                    SetStatus("ГРЕШКА при: " + Path.GetFileName(file) +
                        (tail.Length > 0 ? " — " + tail.Split('\n')[Math.Max(0, tail.Split('\n').Length - 2)].Trim() : ""));
                }
            }
            catch (Exception ex)
            {
                fail++;
                Log("ИЗКЛЮЧЕНИЕ: " + ex);
                SetStatus("ГРЕШКА при: " + Path.GetFileName(file) + " — " + ex.Message);
            }
            finally
            {
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            i++;
            progress.Value = i;
            SetStatus("Готови: " + ok + " | Грешни: " + fail + " | Спестено: " +
                Fmt(saved / 1048576.0) + " (" + i + "/" + total + ")");
        }

        string msg = "Край!\nУспешно: " + ok + "\nГрешни: " + fail + "\nСпестено място: " + Fmt(saved / 1048576.0);
        if (fail > 0) msg += "\n\nВиж лога: " + LogFile;
        Log("===== КРАЙ: успешно " + ok + ", грешни " + fail + ", спестено " + fmtMb(saved) + " =====");
        SetStatus(msg);
        MessageBox.Show(msg, "Готово!");
        btnGo.Enabled = true;
    }

    static string fmtMb(long b)
    {
        double mb = b / 1048576.0;
        return mb >= 1024 ? Math.Round(mb / 1024, 2) + " GB" : Math.Round(mb, 1) + " MB";
    }
}
