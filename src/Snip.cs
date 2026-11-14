using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // ======================= Screen helpers =======================

    static class Shot
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint a);

        public static Rectangle Virtual { get { return SystemInformation.VirtualScreen; } }

        public static Bitmap Capture(Rectangle r)
        {
            var b = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppArgb);
            try { using (var g = Graphics.FromImage(b)) g.CopyFromScreen(r.Location, Point.Empty, r.Size, CopyPixelOperation.SourceCopy); return b; }
            catch { b.Dispose(); throw; }
        }

        // Visible top-level windows in Z order (front first), with their visible frame.
        public static List<Rectangle> WindowRects()
        {
            var list = new List<Rectangle>();
            uint me = (uint)Process.GetCurrentProcess().Id;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || IsIconic(h)) return true;
                int ex = GetWindowLong(h, -20);
                if ((ex & 0x80) != 0) return true; // tool windows
                int cloaked; if (DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                uint pid; Native.GetWindowThreadProcessId(h, out pid);
                if (pid == me) return true;
                RECT r;
                if (DwmGetWindowAttribute(h, 9, out r, 16) != 0 && !GetWindowRect(h, out r)) return true;
                var rect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
                if (rect.Width > 20 && rect.Height > 20) list.Add(rect);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static string NewName(string ext) { return "Snip " + DateTime.Now.ToString("yyyy-MM-dd HHmmssfff") + " " + Guid.NewGuid().ToString("N").Substring(0, 8) + ext; }

        public static string CopyMode(string mode) { return mode == "path" || mode == "both" ? mode : "image"; }
        public static bool NeedsSavedFile(Settings settings) { return settings.SnipAutoSave || CopyMode(settings.SnipCopyMode) != "image"; }
        public static DataObject SnipData(Image image, string path, string mode)
        {
            mode = CopyMode(mode);
            var data = new DataObject();
            if (mode == "image" || mode == "both") data.SetData(DataFormats.Bitmap, true, image);
            if (mode != "image")
            {
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Save the snip before copying its path.");
                path = Path.GetFullPath(path);
                data.SetData(DataFormats.UnicodeText, path);
                if (mode == "both") data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
            }
            return data;
        }

        public static string SnipFolder
        {
            get
            {
                string f = Store.Settings.SnipFolder;
                if (string.IsNullOrWhiteSpace(f)) f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                try { Directory.CreateDirectory(f); } catch { }
                return f;
            }
        }

        public static string VideoFolder
        {
            get
            {
                string f = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings");
                try { Directory.CreateDirectory(f); } catch { }
                return f;
            }
        }
    }

    // ======================= Selection overlay =======================

    // Full-screen frozen picture of the desktop; pick a rectangle, window, screen or free-form area.
    class SnipOverlay : Form
    {
        public string Mode;               // rect | window | full | free
        public Rectangle Result;          // in screen coordinates
        public Bitmap Picture;            // cropped result (null for recording)
        public List<Point> FreePolygon { get { return free.Select(p => new Point(p.X + virt.X, p.Y + virt.Y)).ToList(); } }
        readonly bool forRecording;
        readonly Bitmap frozen, dim;
        readonly Rectangle virt;
        readonly List<Rectangle> windows;
        Point start, cur;
        bool dragging;
        readonly List<Point> free = new List<Point>();
        Rectangle hover = Rectangle.Empty;
        readonly FlowLayoutPanel bar;

        public SnipOverlay(string mode, bool forRecording)
        {
            Mode = mode;
            this.forRecording = forRecording;
            virt = Shot.Virtual;
            windows = Shot.WindowRects();
            frozen = Shot.Capture(virt);
            dim = new Bitmap(frozen);
            using (var g = Graphics.FromImage(dim))
            using (var b = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) g.FillRectangle(b, 0, 0, dim.Width, dim.Height);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virt;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            AutoScaleMode = AutoScaleMode.None;

            bar = new FlowLayoutPanel { AutoSize = true, BackColor = Color.FromArgb(40, 40, 40), Padding = new Padding(Ui.S(4)), WrapContents = true, MaximumSize = new Size(Screen.FromPoint(Cursor.Position).WorkingArea.Width - Ui.S(20), 0) };
            foreach (var m in CaptureCommands.Modes)
            {
                if (forRecording && m[0] == "free") continue;
                string id = m[0];
                var b = new Button { Text = m[1], AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Font = Ui.Base, Margin = new Padding(Ui.S(2)), Tag = id, Cursor = Cursors.Hand };
                b.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
                b.Click += delegate { if (id == "fixed" && !CaptureCommands.FixedSize(false)) return; Mode = id; MarkMode(); Invalidate(); };
                bar.Controls.Add(b);
            }
            var x = new Button { Text = "Cancel (Esc)", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, Font = Ui.Base, Margin = new Padding(Ui.S(2)), Cursor = Cursors.Hand };
            x.Click += delegate { DialogResult = DialogResult.Cancel; };
            bar.Controls.Add(x);
            Controls.Add(bar);
            MarkMode();
            Load += delegate
            {
                var pr = Screen.FromPoint(Cursor.Position).Bounds;
                bar.Location = new Point(pr.Left - virt.Left + (pr.Width - bar.Width) / 2, pr.Top - virt.Top + Ui.S(10));
            };
        }

        void MarkMode()
        {
            foreach (Control c in bar.Controls)
                if (c.Tag != null) c.BackColor = (string)c.Tag == Mode ? Color.FromArgb(0, 103, 192) : Color.FromArgb(60, 60, 60);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(dim, 0, 0);
            Rectangle sel = Rectangle.Empty;
            if (Mode == "rect" && dragging) sel = Norm(start, cur);
            else if ((Mode == "window" || Mode == "full" || Mode == "fixed" || Mode == "control") && hover != Rectangle.Empty) sel = new Rectangle(hover.X - virt.X, hover.Y - virt.Y, hover.Width, hover.Height);
            if (sel.Width > 0 && sel.Height > 0)
            {
                g.DrawImage(frozen, sel, sel, GraphicsUnit.Pixel);
                using (var p = new Pen(Color.FromArgb(0, 150, 255), 2)) g.DrawRectangle(p, sel);
                string size = sel.Width + " x " + sel.Height;
                TextRenderer.DrawText(g, size, Ui.Bold, new Point(sel.X + 4, Math.Max(0, sel.Y - Ui.S(22))), Color.White, Color.FromArgb(0, 103, 192));
            }
            if (Mode == "free" && free.Count > 1)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(free.ToArray());
                    g.SetClip(path);
                    g.DrawImageUnscaled(frozen, 0, 0);
                    g.ResetClip();
                    using (var p = new Pen(Color.FromArgb(0, 150, 255), 2)) g.DrawLines(p, free.ToArray());
                }
            }
        }

        static Rectangle Norm(Point a, Point b)
        {
            return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; return; }
            if (e.Button != MouseButtons.Left) return;
            start = cur = e.Location;
            if (Mode == "rect") dragging = true;
            else if (Mode == "free") { free.Clear(); free.Add(e.Location); dragging = true; }
            else if (hover != Rectangle.Empty) Finish(hover);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            cur = e.Location;
            var screenPt = new Point(e.X + virt.X, e.Y + virt.Y);
            if (Mode == "fixed" || Mode == "control")
            {
                var next = Mode == "fixed" ? CaptureCommands.FixedAt(screenPt, new Size(Store.Settings.FixedW, Store.Settings.FixedH), virt) : CaptureNative.BoundsAt(screenPt);
                if (next != hover) { hover = next; Invalidate(); }
            }
            else if (Mode == "window")
            {
                var w = windows.FirstOrDefault(r => r.Contains(screenPt));
                var nh = w == Rectangle.Empty ? Screen.FromPoint(screenPt).Bounds : w;
                if (nh != hover) { hover = nh; Invalidate(); }
            }
            else if (Mode == "full")
            {
                var nh = Screen.FromPoint(screenPt).Bounds;
                if (nh != hover) { hover = nh; Invalidate(); }
            }
            else if (dragging)
            {
                if (Mode == "free") free.Add(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (Mode == "rect")
            {
                var r = Norm(start, e.Location);
                if (r.Width < 4 || r.Height < 4) { Invalidate(); return; }
                Finish(new Rectangle(r.X + virt.X, r.Y + virt.Y, r.Width, r.Height));
            }
            else if (Mode == "free" && free.Count > 2)
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(free.ToArray());
                    path.CloseFigure();
                    var b = Rectangle.Round(path.GetBounds());
                    b.Intersect(new Rectangle(0, 0, frozen.Width, frozen.Height));
                    if (b.Width < 4 || b.Height < 4) return;
                    Result = new Rectangle(b.X + virt.X, b.Y + virt.Y, b.Width, b.Height);
                    var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        g.TranslateTransform(-b.X, -b.Y);
                        g.SetClip(path);
                        g.DrawImageUnscaled(frozen, 0, 0);
                    }
                    Picture = bmp;
                    DialogResult = DialogResult.OK;
                }
            }
        }

        void Finish(Rectangle screenRect)
        {
            screenRect.Intersect(virt);
            if (screenRect.Width < 2 || screenRect.Height < 2) return;
            Result = screenRect;
            if (!forRecording)
            {
                var local = new Rectangle(screenRect.X - virt.X, screenRect.Y - virt.Y, screenRect.Width, screenRect.Height);
                Picture = frozen.Clone(local, PixelFormat.Format32bppArgb);
            }
            DialogResult = DialogResult.OK;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            if (keyData == Keys.Enter && Mode == "full") { Finish(Screen.FromPoint(Cursor.Position).Bounds); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { frozen.Dispose(); dim.Dispose(); if (Picture != null) { Picture.Dispose(); Picture = null; } }
            base.Dispose(disposing);
        }
    }

    // ======================= OCR (Windows built-in) =======================

    static class Ocr
    {
        const string Script = @"param([string]$img, [string]$lang, [switch]$list)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType=WindowsRuntime]
$null = [Windows.Globalization.Language, Windows.Globalization, ContentType=WindowsRuntime]
if ($list) { [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | ForEach-Object { $_.LanguageTag + '|' + $_.DisplayName }; exit }
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType=WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType=WindowsRuntime]
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($op, [Type]$t) { $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); $null = $task.Wait(-1); $task.Result }
$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($img)) ([Windows.Storage.StorageFile])
$stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
$dec = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
$bmp = Await ($dec.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
if ($lang) { $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new($lang)) }
else { $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages() }
if (-not $engine) { throw 'No OCR engine for this language' }
$res = Await ($engine.RecognizeAsync($bmp)) ([Windows.Media.Ocr.OcrResult])
$res.Lines | ForEach-Object { $_.Text }
$stream.Dispose()
";

        static string ScriptPath()
        {
            string dir = Path.Combine(Store.Dir, "tools");
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, "ocr.ps1");
            if (!File.Exists(p) || File.ReadAllText(p) != Script) File.WriteAllText(p, Script, new UTF8Encoding(true));
            return p;
        }

        static string RunPs(string args, int timeoutMs, out string error)
        {
            error = null;
            var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + ScriptPath() + "\" " + args)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();
                var outTask = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } error = "Timed out."; return null; }
                if (!System.Threading.Tasks.Task.WaitAll(new System.Threading.Tasks.Task[] { errTask, outTask }, 5000)) { error = "Output timed out."; return null; }
                string output = outTask.Result;
                string err = errTask.Result;
                if (p.ExitCode != 0) { error = string.IsNullOrWhiteSpace(err) ? "OCR process failed." : err.Split('\n')[0].Trim(); return null; }
                return output;
            }
        }

        static List<string[]> languages;

        // tag|display pairs, e.g. "ar-SA|Arabic (Saudi Arabia)"
        public static List<string[]> Languages()
        {
            if (languages != null) return languages;
            string err;
            var outp = RunPs("-list", 20000, out err) ?? "";
            languages = outp.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('|')).Where(a => a.Length == 2).ToList();
            return languages;
        }

        // Runs on a background thread; calls done(text, error) on the UI thread of ui.
        public static void Run(Image img, string lang, Control ui, Action<string, string> done)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "snp_ocr_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                // Small text reads much better when enlarged.
                double s = Math.Max(1, Math.Min(3, 1600.0 / Math.Max(img.Width, img.Height)));
                using (var big = new Bitmap(Math.Max(1, (int)(img.Width * s)), Math.Max(1, (int)(img.Height * s))))
                {
                    using (var g = Graphics.FromImage(big)) { g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(img, 0, 0, big.Width, big.Height); }
                    big.Save(tmp, ImageFormat.Png);
                }
            }
            catch (Exception ex) { done(null, ex.Message); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null, text = null;
                try { text = RunPs("-img \"" + tmp + "\"" + (string.IsNullOrEmpty(lang) ? "" : " -lang " + lang), 60000, out err); }
                catch (Exception ex) { err = ex.Message; }
                try { File.Delete(tmp); } catch { }
                text = (text ?? "").Trim();
                try { ui.BeginInvoke(new Action(() => done(text, string.IsNullOrEmpty(text) ? (err ?? "No text found.") : null))); } catch { }
            });
        }
    }

    // ======================= Editor =======================

    class SnipOp
    {
        public string Kind;      // pen, hl, rect, arrow, blur, text, crop
        public Color Color;
        public float Width;
        public List<Point> Pts = new List<Point>();
        public Rectangle Rect;
        public string Text;
    }

    class SnipCanvas : Control
    {
        public SnipEditor Ed;
        public SnipCanvas()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        protected override void OnPaint(PaintEventArgs e) { Ed.PaintCanvas(e.Graphics); }
    }

    class SnipEditor : Form
    {
        Bitmap baseImg, rendered;
        List<SnipOp> ops = new List<SnipOp>();
        readonly Stack<List<SnipOp>> undo = new Stack<List<SnipOp>>(), redo = new Stack<List<SnipOp>>();
        readonly SnipCanvas canvas;
        readonly ToolStrip tools;
        readonly Panel ocrPanel;
        readonly TextBox ocrText;
        readonly ComboBox ocrLang;
        readonly Label ocrStatus, info;
        string tool = "rect";
        Color color = Color.FromArgb(230, 40, 40);
        float width = 4;
        float zoom = 1; bool fit = true;
        Point offset;
        SnipOp live;
        Point dragStart;
        string filePath;
        readonly Dictionary<string, ToolStripButton> toolButtons = new Dictionary<string, ToolStripButton>();

        public SnipEditor(Bitmap img, string savedPath)
        {
            baseImg = img;
            filePath = savedPath;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Snip - " + (savedPath != null ? Path.GetFileName(savedPath) : "not saved");
            Icon = App.AppIcon;
            Font = Ui.Base;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(wa.Width - 80, Math.Max(Ui.S(760), img.Width + Ui.S(60))), Math.Min(wa.Height - 80, Math.Max(Ui.S(520), img.Height + Ui.S(150))));
            Ui.DarkTitle(this);

            canvas = new SnipCanvas { Ed = this, Dock = DockStyle.Fill, BackColor = Ui.Canvas, Cursor = Cursors.Cross };
            canvas.MouseDown += CanvasDown;
            canvas.MouseMove += CanvasMove;
            canvas.MouseUp += CanvasUp;
            canvas.MouseWheel += (s, e) => { if ((ModifierKeys & Keys.Control) != 0) { fit = false; zoom = Math.Max(0.1f, Math.Min(8f, zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f))); canvas.Invalidate(); } };
            canvas.Resize += delegate { canvas.Invalidate(); };
            Controls.Add(canvas);

            ocrPanel = new Panel { Dock = DockStyle.Right, Width = Ui.S(320), Visible = false, Padding = new Padding(Ui.S(8)), BackColor = Ui.Card };
            ocrText = new FluentTextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Ui.Base };
            var ocrBar = LayoutKit.Actions(DockStyle.Bottom);
            ocrBar.Controls.Add(Ui.Btn("Copy all text", delegate { if (App.I.CopyText(ocrText.Text)) ocrStatus.Text = "Copied."; }, true));
            ocrBar.Controls.Add(Ui.Btn("Close", delegate { ocrPanel.Visible = false; }));
            ocrLang = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
            ocrLang.Items.Add("Windows language (auto)");
            ocrLang.SelectedIndex = 0;
            ocrLang.SelectedIndexChanged += delegate
            {
                var tag = ocrLang.SelectedIndex > 0 ? ((string)ocrLang.SelectedItem).Split(' ')[0] : "";
                if (tag != Store.Settings.OcrLang) { Store.Settings.OcrLang = tag; Store.MarkSettings(); if (ocrPanel.Visible) RunOcr(); }
            };
            ocrStatus = new Label { Dock = DockStyle.Top, Height = Ui.S(26), ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.MiddleLeft };
            ocrPanel.Controls.Add(ocrText);
            ocrPanel.Controls.Add(ocrBar);
            ocrPanel.Controls.Add(ocrStatus);
            ocrPanel.Controls.Add(ocrLang);
            ocrPanel.Controls.Add(new Label { Dock = DockStyle.Top, Height = Ui.S(26), Text = "Text from the picture", Font = Ui.Bold });
            Controls.Add(ocrPanel);

            info = new Label { Dock = DockStyle.Bottom, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted, Padding = new Padding(Ui.S(8), 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(info);

            tools = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(Ui.S(6), Ui.S(3), Ui.S(6), Ui.S(3)), Font = Ui.Base, ImageScalingSize = Ui.S(16, 16) };
            Ui.ThemeMenu(tools);
            var snip = new ToolStripDropDownButton("New snip");
            foreach (var m in CaptureCommands.Modes)
            {
                string id = m[0];
                snip.DropDownItems.Add(m[1], null, delegate { Close(); App.I.StartSnip(id, 0, false); });
            }
            snip.DropDownItems.Add("Scrolling Window", null, delegate { Close(); App.I.StartSnip("scroll", 0, false); });
            snip.DropDownItems.Add("Repeat Last Capture", null, delegate { Close(); App.I.StartSnip("repeat", 0, false); });
            snip.DropDownItems.Add(new ToolStripSeparator());
            snip.DropDownItems.Add("New blank image...", null, delegate { NewBlank(); });
            snip.DropDownItems.Add("Open image file...", null, delegate { OpenFile(this); });
            Ui.ThemeMenu(snip.DropDown);
            tools.Items.Add(snip);
            tools.Items.Add(new ToolStripSeparator());
            AddTool("pen", "Pen", "Draw freely (P)");
            AddTool("hl", "Highlighter", "Highlight (H)");
            AddTool("eraser", "Eraser", "Click a drawing to remove it (E)");
            AddTool("rect", "Box", "Draw a rectangle (R)");
            AddTool("arrow", "Arrow", "Draw an arrow (A)");
            AddTool("text", "Text", "Click to type text (T)");
            AddTool("blur", "Hide", "Pixelate private info (B)");
            AddTool("crop", "Crop", "Drag to crop (C)");
            tools.Items.Add(new ToolStripSeparator());
            foreach (var c in new[] { Color.FromArgb(230, 40, 40), Color.FromArgb(255, 200, 0), Color.FromArgb(40, 170, 70), Color.FromArgb(30, 120, 230), Color.Black, Color.White })
            {
                var col = c;
                var b = new ToolStripButton(Ui.Swatch(c, Ui.S(16))) { DisplayStyle = ToolStripItemDisplayStyle.Image, ToolTipText = "Color" };
                b.Click += delegate { color = col; ShowInfo(); };
                tools.Items.Add(b);
            }
            var wd = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(70), ToolTipText = "Thickness" };
            wd.Items.AddRange(new object[] { "Thin", "Medium", "Thick" });
            wd.SelectedIndex = 1;
            wd.SelectedIndexChanged += delegate { width = wd.SelectedIndex == 0 ? 2 : wd.SelectedIndex == 1 ? 4 : 8; };
            tools.Items.Add(wd);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Undo", null, delegate { Undo(); }) { ToolTipText = "Ctrl+Z" });
            tools.Items.Add(new ToolStripButton("Redo", null, delegate { Redo(); }) { ToolTipText = "Ctrl+Y" });
            tools.Items.Add(new ToolStripButton("Fit / 100%", null, delegate { fit = !fit; zoom = 1; canvas.Invalidate(); }) { ToolTipText = "Ctrl+wheel to zoom" });
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Text extract", null, delegate { ShowOcr(); }) { ToolTipText = "Read the text in the picture (Ctrl+T)" });
            tools.Items.Add(new ToolStripButton("Copy", null, delegate { CopyImage(); }) { ToolTipText = "Ctrl+C" });
            tools.Items.Add(new ToolStripButton("Copy path", null, delegate { CopyPath(); }) { ToolTipText = "Ctrl+Shift+C (save first if needed)" });
            tools.Items.Add(new ToolStripButton("Save", null, delegate { Save(false); }) { ToolTipText = "Ctrl+S" });
            tools.Items.Add(new ToolStripButton("Save as", null, delegate { Save(true); }) { ToolTipText = "Ctrl+Shift+S" });
            tools.Items.Add(new ToolStripButton("Pin", null, delegate { new PinnedImage(Render()).Show(); }) { ToolTipText = "Keep this picture on top of the screen (Ctrl+P)" });
            tools.Items.Add(new ToolStripButton("Copy file", null, delegate { CopyFile(); }) { ToolTipText = "Copy the picture file, to paste into mail, chat or a folder" });
            tools.Items.Add(new ToolStripButton("Print", null, delegate { Print(); }) { ToolTipText = "Print the picture fitted to the page" });
            tools.Items.Add(new ToolStripButton("Show in folder", null, delegate { OpenFolder(); }));
            foreach (ToolStripItem i in tools.Items) i.ForeColor = Ui.Text;
            Controls.Add(tools);
            LayoutKit.FlowTools(tools, this);
            Resize += delegate { ocrPanel.Dock = ClientSize.Width < Ui.S(1000) ? DockStyle.Bottom : DockStyle.Right; if (ocrPanel.Dock == DockStyle.Bottom) ocrPanel.Height = Math.Max(Ui.S(100), ClientSize.Height / 3); };
            SelectTool("rect");
            Rerender();
            Ui.Theme(this);
        }

        void AddTool(string id, string text, string tip)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, CheckOnClick = false };
            b.Click += delegate { SelectTool(id); };
            toolButtons[id] = b;
            tools.Items.Add(b);
        }

        void SelectTool(string id)
        {
            tool = id;
            foreach (var kv in toolButtons) kv.Value.Checked = kv.Key == id;
            canvas.Cursor = id == "eraser" ? Cursors.Hand : id == "text" ? Cursors.IBeam : Cursors.Cross;
            ShowInfo();
        }

        void ShowInfo()
        {
            var r = rendered;
            info.Text = (r != null ? r.Width + " x " + r.Height + " px    " : "") + "Tool: " + tool + "    Ctrl+Z undo  ·  Ctrl+C copy  ·  Ctrl+S save  ·  Ctrl+T text  ·  Ctrl+P pin  ·  Ctrl+N new snip" + (filePath != null ? "    Saved: " + filePath : "");
        }

        // ---------- Rendering ----------

        Bitmap Render()
        {
            Bitmap c = new Bitmap(baseImg);
            foreach (var op in ops)
            {
                if (op.Kind == "crop")
                {
                    var r = Rectangle.Intersect(op.Rect, new Rectangle(0, 0, c.Width, c.Height));
                    if (r.Width < 2 || r.Height < 2) continue;
                    var n = c.Clone(r, PixelFormat.Format32bppArgb);
                    c.Dispose();
                    c = n;
                    continue;
                }
                if (op.Kind == "blur") { Pixelate(c, op.Rect); continue; }
                using (var g = Graphics.FromImage(c)) DrawOp(g, op);
            }
            return c;
        }

        void Rerender()
        {
            if (rendered != null) rendered.Dispose();
            rendered = Render();
            canvas.Invalidate();
            ShowInfo();
        }

        static void Pixelate(Bitmap c, Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, c.Width, c.Height));
            if (r.Width < 2 || r.Height < 2) return;
            int block = Math.Max(6, Math.Min(r.Width, r.Height) / 8);
            using (var part = c.Clone(r, PixelFormat.Format32bppArgb))
            using (var small = new Bitmap(Math.Max(1, r.Width / block), Math.Max(1, r.Height / block)))
            using (var g = Graphics.FromImage(c))
            {
                using (var gs = Graphics.FromImage(small)) { gs.InterpolationMode = InterpolationMode.HighQualityBilinear; gs.DrawImage(part, 0, 0, small.Width, small.Height); }
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(small, r);
            }
        }

        static void DrawOp(Graphics g, SnipOp op)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (op.Kind)
            {
                case "pen":
                case "hl":
                    if (op.Pts.Count < 2) { if (op.Pts.Count == 1) using (var b = new SolidBrush(op.Color)) g.FillEllipse(b, op.Pts[0].X - op.Width / 2, op.Pts[0].Y - op.Width / 2, op.Width, op.Width); return; }
                    using (var p = new Pen(op.Kind == "hl" ? Color.FromArgb(110, op.Color) : op.Color, op.Kind == "hl" ? op.Width * 4 : op.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(p, op.Pts.ToArray());
                    return;
                case "rect":
                    using (var p = new Pen(op.Color, op.Width)) g.DrawRectangle(p, op.Rect);
                    return;
                case "arrow":
                    if (op.Pts.Count < 2) return;
                    using (var cap = new AdjustableArrowCap(4, 4, true))
                    using (var p = new Pen(op.Color, op.Width) { CustomEndCap = cap, StartCap = LineCap.Round })
                        g.DrawLine(p, op.Pts[0], op.Pts[op.Pts.Count - 1]);
                    return;
                case "text":
                    using (var f = new Font("Segoe UI Semibold", 10 + op.Width * 2.5f, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(op.Color))
                    {
                        using (var fmt = Ui.IsRtl(op.Text) ? new StringFormat(StringFormatFlags.DirectionRightToLeft) : new StringFormat())
                        {
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.DrawString(op.Text, f, b, op.Pts[0], fmt);
                        }
                    }
                    return;
            }
        }

        Rectangle ImageRect()
        {
            if (rendered == null) return Rectangle.Empty;
            float z = zoom;
            if (fit) z = Math.Min(1f, Math.Min((canvas.Width - 20f) / rendered.Width, (canvas.Height - 20f) / rendered.Height));
            if (z <= 0) z = 0.05f;
            zoom = z;
            int w = (int)(rendered.Width * z), h = (int)(rendered.Height * z);
            offset = new Point(Math.Max(10, (canvas.Width - w) / 2), Math.Max(10, (canvas.Height - h) / 2));
            return new Rectangle(offset.X, offset.Y, w, h);
        }

        public void PaintCanvas(Graphics g)
        {
            g.Clear(canvas.BackColor);
            if (rendered == null) return;
            var r = ImageRect();
            g.InterpolationMode = zoom < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.DrawImage(rendered, r);
            using (var p = new Pen(Color.FromArgb(80, 0, 0, 0))) g.DrawRectangle(p, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
            if (live != null)
            {
                var st = g.Save();
                g.TranslateTransform(offset.X, offset.Y);
                g.ScaleTransform(zoom, zoom);
                if (live.Kind == "crop" || live.Kind == "blur")
                    using (var p = new Pen(Color.FromArgb(0, 150, 255), 2 / zoom) { DashStyle = DashStyle.Dash }) g.DrawRectangle(p, live.Rect);
                else DrawOp(g, live);
                g.Restore(st);
            }
        }

        Point ToImage(Point p)
        {
            return new Point((int)((p.X - offset.X) / zoom), (int)((p.Y - offset.Y) / zoom));
        }

        static Rectangle Norm(Point a, Point b) { return Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)); }

        void Push(List<SnipOp> next)
        {
            undo.Push(ops);
            redo.Clear();
            ops = next;
            Rerender();
        }

        void Undo() { if (undo.Count == 0) return; redo.Push(ops); ops = undo.Pop(); Rerender(); }
        void Redo() { if (redo.Count == 0) return; undo.Push(ops); ops = redo.Pop(); Rerender(); }

        void CanvasDown(object sender, MouseEventArgs e)
        {
            canvas.Focus();
            if (e.Button != MouseButtons.Left) return;
            var p = ToImage(e.Location);
            dragStart = p;
            if (tool == "eraser") { Erase(p); return; }
            if (tool == "text")
            {
                var t = Ui.Ask("Add text", "Text to put on the picture:", "", this);
                if (!string.IsNullOrEmpty(t)) { var op = new SnipOp { Kind = "text", Color = color, Width = width, Text = t }; op.Pts.Add(p); var n = ops.ToList(); n.Add(op); Push(n); }
                return;
            }
            live = new SnipOp { Kind = tool, Color = color, Width = width };
            live.Pts.Add(p);
            live.Rect = new Rectangle(p, Size.Empty);
        }

        void CanvasMove(object sender, MouseEventArgs e)
        {
            if (live == null) return;
            var p = ToImage(e.Location);
            if (live.Kind == "pen" || live.Kind == "hl") live.Pts.Add(p);
            else if (live.Kind == "arrow") { if (live.Pts.Count > 1) live.Pts[1] = p; else live.Pts.Add(p); }
            else live.Rect = Norm(dragStart, p);
            canvas.Invalidate();
        }

        void CanvasUp(object sender, MouseEventArgs e)
        {
            if (live == null) return;
            var op = live;
            live = null;
            if ((op.Kind == "rect" || op.Kind == "crop" || op.Kind == "blur") && (op.Rect.Width < 3 || op.Rect.Height < 3)) { canvas.Invalidate(); return; }
            if (op.Kind == "arrow" && op.Pts.Count < 2) { canvas.Invalidate(); return; }
            var n = ops.ToList();
            n.Add(op);
            Push(n);
            if (op.Kind == "crop") SelectTool("rect");
        }

        void Erase(Point p)
        {
            for (int i = ops.Count - 1; i >= 0; i--)
            {
                var op = ops[i];
                if (op.Kind == "crop") return; // cannot erase through a crop
                Rectangle b;
                if (op.Kind == "rect" || op.Kind == "blur") b = op.Rect;
                else if (op.Kind == "text") b = new Rectangle(op.Pts[0], new Size(Math.Max(40, op.Text.Length * 12), 40));
                else
                {
                    int x0 = op.Pts.Min(q => q.X), y0 = op.Pts.Min(q => q.Y), x1 = op.Pts.Max(q => q.X), y1 = op.Pts.Max(q => q.Y);
                    b = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
                }
                b.Inflate((int)op.Width * 3 + 4, (int)op.Width * 3 + 4);
                if (b.Contains(p)) { var n = ops.ToList(); n.RemoveAt(i); Push(n); return; }
            }
        }

        // ---------- Actions ----------

        void CopyImage()
        {
            using (var img = Render())
            {
                try { Clipboard.SetImage(img); info.Text = "Copied to the clipboard."; } catch (Exception ex) { info.Text = "Copy failed: " + ex.Message; }
            }
        }

        public bool Save(bool asNew)
        {
            string path = filePath;
            if (asNew || path == null)
            {
                using (var d = new SaveFileDialog())
                {
                    d.Filter = "PNG picture|*.png|JPEG picture|*.jpg|Bitmap|*.bmp|GIF|*.gif";
                    d.InitialDirectory = Shot.SnipFolder;
                    d.FileName = path != null ? Path.GetFileName(path) : Shot.NewName(".png");
                    if (d.ShowDialog(this) != DialogResult.OK) return false;
                    path = d.FileName;
                }
            }
            try
            {
                using (var img = Render())
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    var fmt = ext == ".jpg" || ext == ".jpeg" ? ImageFormat.Jpeg : ext == ".bmp" ? ImageFormat.Bmp : ext == ".gif" ? ImageFormat.Gif : ImageFormat.Png;
                    img.Save(path, fmt);
                }
                filePath = path;
                Text = "Snip - " + Path.GetFileName(path);
                info.Text = "Saved: " + path;
                SnipPanel.RaiseChanged();
                return true;
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save: " + ex.Message, App.Name); return false; }
        }

        void CopyPath()
        {
            if ((string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) && !Save(false)) return;
            if (App.I.CopyText(Path.GetFullPath(filePath))) info.Text = "File path copied.";
        }

        void CopyFile()
        {
            if ((string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) && !Save(false)) return;
            try
            {
                App.I.Suppress();
                Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { Path.GetFullPath(filePath) });
                info.Text = "File copied. Paste it into a mail, chat or folder.";
            }
            catch (Exception ex) { info.Text = "Copy failed: " + ex.Message; }
        }

        void Print()
        {
            Bitmap img = null;
            try
            {
                img = Render();
                using (var doc = new System.Drawing.Printing.PrintDocument())
                using (var dlg = new PrintDialog { Document = doc, UseEXDialog = true })
                {
                    doc.DocumentName = Text;
                    if (img.Width > img.Height) doc.DefaultPageSettings.Landscape = true;
                    var pic = img;
                    doc.PrintPage += (s, e) =>
                    {
                        var m = e.MarginBounds;
                        float k = Math.Min(1f, Math.Min(m.Width / (float)pic.Width, m.Height / (float)pic.Height));
                        float w = pic.Width * k, h = pic.Height * k;
                        e.Graphics.DrawImage(pic, m.Left + (m.Width - w) / 2, m.Top + (m.Height - h) / 2, w, h);
                    };
                    if (dlg.ShowDialog(this) == DialogResult.OK) { doc.Print(); info.Text = "Sent to the printer."; }
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not print: " + ex.Message, App.Name); }
            finally { if (img != null) img.Dispose(); }
        }

        public static void NewBlank()
        {
            using (var f = Ui.Dialog("New blank image", 360, 230))
            {
                var fields = LayoutKit.Fields();
                var w = new NumericUpDown { Minimum = 16, Maximum = 8000, Value = 800 };
                var h = new NumericUpDown { Minimum = 16, Maximum = 8000, Value = 600 };
                LayoutKit.Add(fields, new WrapLabel { Text = "Width in pixels" }, false); LayoutKit.Add(fields, w, false);
                LayoutKit.Add(fields, new WrapLabel { Text = "Height in pixels" }, false); LayoutKit.Add(fields, h, false);
                var ok = Ui.Btn("Create", null, true); ok.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(fields); f.Controls.Add(LayoutKit.Actions(DockStyle.Bottom, ok, cancel)); f.AcceptButton = ok; f.CancelButton = cancel; Ui.Theme(f);
                if (f.ShowDialog() != DialogResult.OK) return;
                var img = new Bitmap((int)w.Value, (int)h.Value, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(img)) g.Clear(Color.White);
                var ed = new SnipEditor(img, null); ed.Show(); ed.Activate();
            }
        }

        public static void OpenFile(IWin32Window owner)
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*";
                d.InitialDirectory = Shot.SnipFolder;
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    Bitmap img;
                    // Copy into memory so the file is not kept locked while editing.
                    using (var fs = new FileStream(d.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var src = Image.FromStream(fs))
                    {
                        img = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                        using (var g = Graphics.FromImage(img)) { g.Clear(Color.White); g.DrawImage(src, 0, 0, src.Width, src.Height); }
                    }
                    var ed = new SnipEditor(img, d.FileName); ed.Show(); ed.Activate();
                }
                catch (Exception ex) { MessageBox.Show("Could not open the picture: " + ex.Message, App.Name); }
            }
        }

        void OpenFolder()
        {
            try
            {
                if (filePath != null && File.Exists(filePath)) Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
                else Process.Start("explorer.exe", "\"" + Shot.SnipFolder + "\"");
            }
            catch { }
        }

        public void ShowOcr()
        {
            ocrPanel.Visible = true;
            if (ocrLang.Items.Count == 1)
            {
                ocrStatus.Text = "Loading languages...";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    var langs = Ocr.Languages();
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            foreach (var l in langs) ocrLang.Items.Add(l[0] + "  " + l[1]);
                            int idx = langs.FindIndex(l => l[0] == Store.Settings.OcrLang);
                            if (idx >= 0) ocrLang.SelectedIndex = idx + 1;
                            RunOcr();
                        }));
                    }
                    catch { }
                });
            }
            else RunOcr();
        }

        void RunOcr()
        {
            ocrStatus.Text = "Reading text...";
            ocrText.Text = "";
            using (var img = Render())
                Ocr.Run(img, Store.Settings.OcrLang, this, (text, err) =>
                {
                    if (IsDisposed) return;
                    ocrText.Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                    ocrText.RightToLeft = Ui.IsRtl(ocrText.Text) ? RightToLeft.Yes : RightToLeft.No;
                    ocrStatus.Text = err ?? ("Found " + ocrText.Lines.Length + " line(s). Edit freely, then Copy all text.");
                });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ActiveControl == ocrText || (ocrText.Focused)) return base.ProcessCmdKey(ref msg, keyData);
            switch (keyData)
            {
                case Keys.Control | Keys.Z: Undo(); return true;
                case Keys.Control | Keys.Y: Redo(); return true;
                case Keys.Control | Keys.C: CopyImage(); return true;
                case Keys.Control | Keys.Shift | Keys.C: CopyPath(); return true;
                case Keys.Control | Keys.S: Save(false); return true;
                case Keys.Control | Keys.Shift | Keys.S: Save(true); return true;
                case Keys.Control | Keys.T: ShowOcr(); return true;
                case Keys.Control | Keys.P: new PinnedImage(Render()).Show(); return true;
                case Keys.Control | Keys.N: Close(); App.I.StartSnip("rect", 0, false); return true;
                case Keys.P: SelectTool("pen"); return true;
                case Keys.H: SelectTool("hl"); return true;
                case Keys.E: SelectTool("eraser"); return true;
                case Keys.R: SelectTool("rect"); return true;
                case Keys.A: SelectTool("arrow"); return true;
                case Keys.T: SelectTool("text"); return true;
                case Keys.B: SelectTool("blur"); return true;
                case Keys.C: SelectTool("crop"); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (rendered != null) rendered.Dispose();
            baseImg.Dispose();
            Ui.ClearItems(tools.Items);
            base.OnFormClosed(e);
            Ui.Later(1500, App.I.Trim);
        }
    }

    // Picture pinned on top of everything. Drag to move, wheel to zoom, double-click or Esc to close.
    class PinnedImage : Form
    {
        readonly Image img;
        float z = 1;
        public PinnedImage(Image image)
        {
            img = image;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            z = Math.Min(1f, Math.Min(wa.Width * 0.6f / img.Width, wa.Height * 0.6f / img.Height));
            Size = new Size((int)(img.Width * z) + 2, (int)(img.Height * z) + 2);
            Location = new Point(Cursor.Position.X - Width / 2, Cursor.Position.Y - Height / 2);
            BackColor = Color.FromArgb(0, 120, 215);
            var m = Ui.Menu();
            m.Items.Add("Copy", null, delegate { try { Clipboard.SetImage(img); } catch { } });
            m.Items.Add("Opacity 100%", null, delegate { Opacity = 1; });
            m.Items.Add("Opacity 70%", null, delegate { Opacity = 0.7; });
            m.Items.Add("Opacity 40%", null, delegate { Opacity = 0.4; });
            m.Items.Add("Close", null, delegate { Close(); });
            Ui.ThemeMenu(m);
            ContextMenuStrip = m;
            Ui.Theme(this);
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= Native.CS_DROPSHADOW; cp.ExStyle |= 0x80; return cp; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(img, 1, 1, Width - 2, Height - 2);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Clicks == 1) { Native.ReleaseCapture(); Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { Close(); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            z = Math.Max(0.1f, Math.Min(4f, z * (e.Delta > 0 ? 1.1f : 1 / 1.1f)));
            Size = new Size((int)(img.Width * z) + 2, (int)(img.Height * z) + 2);
            Invalidate();
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.C)) { try { Clipboard.SetImage(img); } catch { } return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { img.Dispose(); base.OnFormClosed(e); }
    }

    // ======================= Screen recording (ffmpeg) =======================

    static class Recorder
    {
        static Process ff;
        public static bool Saving;
        public static event Action Finished;
        static readonly List<string> segs = new List<string>();
        static Rectangle area;
        static string outPath;
        static RecordBar bar;
        static RecordBorder border;
        static readonly Stopwatch watch = new Stopwatch();
        public static bool Active { get { return bar != null; } }
        public static bool Paused;
        static string ffmpeg, audioDevice;
        static bool mic;
        static bool starting, changing, stopRequested, keepRequested;
        static int session;
        static string recordDevice;
        static readonly object audioGate = new object();

        public static string FindFfmpeg()
        {
            if (ffmpeg != null && File.Exists(ffmpeg)) return ffmpeg;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { var p = Path.Combine(dir.Trim(), "ffmpeg.exe"); if (File.Exists(p)) return ffmpeg = p; } catch { }
            }
            try
            {
                string wg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Packages");
                if (Directory.Exists(wg))
                    foreach (var d in Directory.GetDirectories(wg, "*FFmpeg*"))
                    {
                        var f = Directory.GetFiles(d, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                        if (f != null) return ffmpeg = f;
                    }
            }
            catch { }
            return null;
        }

        static string FindMic(string exe)
        {
            lock (audioGate)
            {
            if (audioDevice != null) return audioDevice.Length == 0 ? null : audioDevice;
            audioDevice = "";
            try
            {
                var psi = new ProcessStartInfo(exe, "-hide_banner -list_devices true -f dshow -i dummy") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8 };
                using (var p = Process.Start(psi))
                {
                    var read = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return null; }
                    if (!read.Wait(1000)) return null;
                    string err = read.Result;
                    foreach (var line in err.Split('\n'))
                    {
                        if (!line.Contains("(audio)")) continue;
                        int a = line.IndexOf('"'), b = line.IndexOf('"', a + 1);
                        if (a >= 0 && b > a) { audioDevice = line.Substring(a + 1, b - a - 1); break; }
                    }
                }
            }
            catch { }
            return audioDevice.Length == 0 ? null : audioDevice;
            }
        }

        public static void Start(Rectangle r, bool withMic)
        {
            if (Active || Saving) return;
            string exe = FindFfmpeg();
            if (exe == null)
            {
                MessageBox.Show("Screen recording uses the free ffmpeg tool, which was not found.\n\nInstall it once with:\nwinget install Gyan.FFmpeg", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            area = r;
            area.Width -= area.Width % 2;
            area.Height -= area.Height % 2;
            mic = withMic;
            int currentSession = ++session;
            recordDevice = null;
            segs.Clear();
            outPath = Path.Combine(Shot.VideoFolder, "Recording " + DateTime.Now.ToString("yyyy-MM-dd HHmmssfff") + " " + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mp4");
            Paused = false;
            watch.Reset();
            border = new RecordBorder(area);
            border.Show();
            bar = new RecordBar(area);
            bar.Show();
            starting = withMic;
            if (withMic)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string device = FindMic(exe);
                    Post(() => { if (currentSession != session || !Active) return; recordDevice = device; starting = false; BeginSegment(); });
                });
            }
            else BeginSegment();
        }

        static void BeginSegment()
        {
            try { StartSegment(); }
            catch (Exception ex) { Stop(false); App.I.Balloon("Could not start recording", ex.Message); }
        }

        static void Post(Action action) { try { App.I.Sync.BeginInvoke(action); } catch { } }

        static void StartSegment()
        {
            string exe = FindFfmpeg();
            string seg = Path.Combine(Path.GetTempPath(), "snp_rec_" + Guid.NewGuid().ToString("N") + ".mp4");
            segs.Add(seg);
            var args = new StringBuilder("-hide_banner -loglevel error -y ");
            args.AppendFormat("-f gdigrab -framerate {0} -offset_x {1} -offset_y {2} -video_size {3}x{4} -draw_mouse 1 -i desktop ",
                Math.Max(5, Math.Min(60, Store.Settings.RecordFps)), area.X, area.Y, area.Width, area.Height);
            string dev = mic ? recordDevice : null;
            if (dev != null) args.AppendFormat("-f dshow -i audio=\"{0}\" ", dev);
            args.Append("-c:v libx264 -preset ultrafast -crf 24 -pix_fmt yuv420p ");
            if (dev != null) args.Append("-c:a aac -b:a 128k ");
            args.AppendFormat("\"{0}\"", seg);
            var psi = new ProcessStartInfo(exe, args.ToString()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardError = true };
            ff = Process.Start(psi);
            ff.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) App.Log("ffmpeg: " + e.Data); };
            ff.BeginErrorReadLine();
            watch.Start();
        }

        static bool StopSegment(Process segment)
        {
            if (segment == null) return true;
            try
            {
                try { segment.StandardInput.Write("q"); segment.StandardInput.Flush(); } catch { }
                if (!segment.WaitForExit(6000)) { try { segment.Kill(); segment.WaitForExit(2000); } catch { } return false; }
                return segment.ExitCode == 0;
            }
            finally { segment.Dispose(); }
        }

        public static TimeSpan Elapsed { get { return watch.Elapsed; } }

        public static void TogglePause()
        {
            if (!Active || starting || changing) return;
            if (Paused) { try { StartSegment(); Paused = false; } catch (Exception ex) { App.I.Balloon("Could not resume recording", ex.Message); } }
            else
            {
                Paused = true; changing = true; watch.Stop();
                var segment = ff; ff = null;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { StopSegment(segment); } catch (Exception ex) { App.Log("Recording pause: " + ex.Message); }
                    finally { Post(() => { changing = false; if (stopRequested) { stopRequested = false; Stop(keepRequested); } }); }
                });
            }
        }

        public static void Stop(bool keep)
        {
            if (!Active) return;
            if (changing) { stopRequested = true; keepRequested = keep; return; }
            starting = false; session++;
            watch.Stop();
            var segment = ff; ff = null;
            var b = bar; bar = null;
            if (b != null) b.Close();
            if (border != null) { border.Close(); border = null; }
            var paths = segs.ToList();
            Saving = true;
            string final = outPath;
            uint copySequence = Native.GetClipboardSequenceNumber();
            ThreadPool.QueueUserWorkItem(delegate
            {
                var parts = new List<string>();
                try
                {
                    bool complete = StopSegment(segment);
                    parts = paths.Where(File.Exists).ToList();
                    if (!keep) { foreach (var s in parts) try { File.Delete(s); } catch { } return; }
                    if (!complete) throw new IOException("Recording did not finish cleanly; source segments were kept in the temporary folder.");
                    if (parts.Count == 0) return;
                    if (parts.Count == 1) File.Move(parts[0], final);
                    else
                    {
                        string list = Path.Combine(Path.GetTempPath(), "snp_rec_list_" + Guid.NewGuid().ToString("N") + ".txt");
                        File.WriteAllLines(list, parts.Select(s => "file '" + s.Replace("'", "'\\''") + "'"));
                        try
                        {
                            var psi = new ProcessStartInfo(FindFfmpeg(), "-hide_banner -loglevel error -n -f concat -safe 0 -i \"" + list + "\" -c copy \"" + final + "\"") { UseShellExecute = false, CreateNoWindow = true };
                            using (var p = Process.Start(psi))
                            {
                                if (!p.WaitForExit(120000)) { try { p.Kill(); p.WaitForExit(2000); } catch { } throw new IOException("Recording merge timed out; source segments were kept in the temporary folder."); }
                                if (p.ExitCode != 0 || !File.Exists(final) || new FileInfo(final).Length == 0) throw new IOException("Recording merge failed; source segments were kept in the temporary folder.");
                            }
                            foreach (var s in parts) try { File.Delete(s); } catch { }
                        }
                        finally { try { File.Delete(list); } catch { } }
                    }
                    App.I.Sync.BeginInvoke(new Action(() =>
                    {
                        var sc = new System.Collections.Specialized.StringCollection { final };
                        bool copied = false;
                        if (Native.GetClipboardSequenceNumber() == copySequence) { try { Clipboard.SetFileDropList(sc); copied = true; } catch { } }
                        App.I.Balloon("Recording saved", Path.GetFileName(final) + (copied ? " - copied, paste it into a chat or folder." : " - saved in Screen Recordings."));
                        SnipPanel.RaiseChanged();
                    }));
                }
                catch (Exception ex)
                {
                    App.Log("Recording save failed: " + ex.Message + " Segments: " + string.Join(", ", paths));
                    try { App.I.Sync.BeginInvoke(new Action(() => App.I.Balloon("Recording save failed", ex.Message))); } catch { }
                }
                finally { try { App.I.Sync.BeginInvoke(new Action(NotifyFinished)); } catch { Saving = false; } }
            });
        }

        static void NotifyFinished() { Saving = false; var h = Finished; if (h != null) h(); }
    }

    // Floating control bar shown while recording (hidden from the recording itself).
    class RecordBar : Form
    {
        readonly Label time;
        readonly Button pause;
        readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();

        public RecordBar(Rectangle area)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(36, 36, 36);
            ForeColor = Color.White;
            Font = Ui.Base;
            StartPosition = FormStartPosition.Manual;
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(Ui.S(6), Ui.S(4), Ui.S(6), 0) };
            var dot = new Label { Text = "●", ForeColor = Color.FromArgb(235, 60, 60), AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, Ui.S(2), 0, 0) };
            time = new Label { Text = "00:00", AutoSize = true, Font = Ui.Bold, Margin = new Padding(Ui.S(2), Ui.S(6), Ui.S(8), 0) };
            pause = Btn("Pause", delegate { Recorder.TogglePause(); pause.Text = Recorder.Paused ? "Resume" : "Pause"; });
            var stop = Btn("Stop & save", delegate { Recorder.Stop(true); });
            var cancel = Btn("Discard", delegate { Recorder.Stop(false); });
            flow.Controls.AddRange(new Control[] { dot, time, pause, stop, cancel });
            Controls.Add(flow);
            Size = Ui.S(330, 40);
            var y = area.Top - Height - Ui.S(6);
            var wa = Screen.FromRectangle(area).Bounds;
            if (y < wa.Top) y = area.Top + Ui.S(6);
            Location = new Point(Math.Max(wa.Left, Math.Min(wa.Right - Width, area.Left + (area.Width - Width) / 2)), Math.Max(wa.Top, Math.Min(wa.Bottom - Height, y)));
            t.Interval = 500;
            t.Tick += delegate { var e = Recorder.Elapsed; time.Text = ((int)e.TotalMinutes).ToString("00") + ":" + e.Seconds.ToString("00"); };
            t.Start();
            HandleCreated += delegate { Shot.SetWindowDisplayAffinity(Handle, 0x11); }; // WDA_EXCLUDEFROMCAPTURE
        }

        static Button Btn(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60, 60, 60), Margin = new Padding(0, 0, Ui.S(4), 0), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            b.Click += click;
            return b;
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && Recorder.Active) { e.Cancel = true; Ui.Later(1, () => Recorder.Stop(true)); }
            base.OnFormClosing(e);
        }
        protected override void OnMouseDown(MouseEventArgs e) { Native.ReleaseCapture(); Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero); }
        protected override void OnFormClosed(FormClosedEventArgs e) { t.Dispose(); base.OnFormClosed(e); }
    }

    // Red frame around the recorded area; click-through and hidden from the recording.
    class RecordBorder : Form
    {
        public RecordBorder(Rectangle area)
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            var r = area; r.Inflate(3, 3);
            Bounds = r;
            HandleCreated += delegate { Shot.SetWindowDisplayAffinity(Handle, 0x11); };
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x20 | 0x80000 | 0x08000000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var p = new Pen(Color.FromArgb(235, 50, 50), 3)) e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
    }

    // ======================= Snip page / card =======================

    class SnipPanel : LivePanel
    {
        public static event Action Changed;
        public static void RaiseChanged() { var h = Changed; if (h != null) h(); }

        readonly ComboBox delay;
        readonly CheckBox micBox;
        readonly CardList list;
        readonly Action<string> status;
        readonly Action onChanged;
        readonly Dictionary<string, Image> thumbs = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        bool stale = true;

        public SnipPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;

            list = new CardList { Dock = DockStyle.Fill };
            list.SelectionMode = SelectionMode.MultiExtended;
            list.ItemHeight = Math.Min(255, Ui.S(64));
            list.Info = o =>
            {
                var f = (FileInfo)o;
                var ci = new CardInfo { Title = f.Name, Meta = Ui.Ago(f.LastWriteTime.Ticks), Sub = Fmt.Bytes(f.Length) + "  ·  " + f.DirectoryName };
                if (IsImage(f.FullName)) ci.Thumb = Thumb(f.FullName); else ci.Strip = Color.FromArgb(235, 60, 60);
                return ci;
            };
            list.DoubleClick += delegate { OpenSel(); };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { OpenSel(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Delete) { DeleteSel(); e.SuppressKeyPress = true; } };
            var m = Ui.Menu();
            m.Items.Add("Open / edit", null, delegate { OpenSel(); });
            m.Items.Add("Copy", null, delegate { CopySel(); });
            m.Items.Add("Copy as path", null, delegate { var selected = list.Selected<FileInfo>(); if (selected.Count > 0 && App.I.CopyText(string.Join("\r\n", selected.Select(f => f.FullName)))) this.status("File path(s) copied."); });
            m.Items.Add("Text extract (OCR)", null, delegate { var f = list.SelectedItem as FileInfo; if (f != null && IsImage(f.FullName)) { var ed = OpenEditor(f.FullName); if (ed != null) ed.ShowOcr(); } });
            m.Items.Add("Pin on screen", null, delegate { var f = list.SelectedItem as FileInfo; if (f != null && IsImage(f.FullName)) { var img = Load(f.FullName); if (img != null) new PinnedImage(img).Show(); } });
            m.Items.Add("Show in folder", null, delegate { var f = list.SelectedItem as FileInfo; if (f != null) try { Process.Start("explorer.exe", "/select,\"" + f.FullName + "\""); } catch { } });
            m.Items.Add("Delete", null, delegate { DeleteSel(); });
            Ui.ThemeMenu(m);
            list.ContextMenuStrip = m;
            Controls.Add(list);

            var rec = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 40), WrapContents = true };
            rec.Controls.Add(Ui.Btn("●  Record area", delegate { App.I.StartRecord("rect"); }));
            rec.Controls.Add(Ui.Btn("Record window", delegate { App.I.StartRecord("window"); }));
            rec.Controls.Add(Ui.Btn("Record full screen", delegate { App.I.StartRecord("full"); }));
            micBox = new CheckBox { Text = "Microphone", AutoSize = true, Checked = Store.Settings.RecordMic, Margin = new Padding(Ui.S(6), Ui.S(7), 0, 0) };
            micBox.CheckedChanged += delegate { Store.Settings.RecordMic = micBox.Checked; Store.MarkSettings(); };
            rec.Controls.Add(micBox);
            Controls.Add(rec);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 0, 0, Ui.S(4)) };
            bar.Controls.Add(Ui.Btn("+  New snip", delegate { App.I.StartSnip("rect", Delay(), false); }, true));
            bar.Controls.Add(Ui.Btn("Window", delegate { App.I.StartSnip("window", Delay(), false); }));
            bar.Controls.Add(Ui.Btn("Full screen", delegate { App.I.StartSnip("full", Delay(), false); }));
            bar.Controls.Add(Ui.Btn("Fixed Region", delegate { App.I.StartSnip("fixed", Delay(), false); }));
            bar.Controls.Add(Ui.Btn("Fixed size...", delegate { CaptureCommands.FixedSize(true); }));
            bar.Controls.Add(Ui.Btn("Scrolling Window", delegate { App.I.StartSnip("scroll", Delay(), false); }));
            bar.Controls.Add(Ui.Btn("Window Control", delegate { App.I.StartSnip("control", Delay(), false); }));
            bar.Controls.Add(Ui.Btn("Repeat Last Capture", delegate { App.I.StartSnip("repeat", 0, false); }));
            bar.Controls.Add(Ui.Btn("Free-form", delegate { App.I.StartSnip("free", Delay(), false); }));
            bar.Controls.Add(Ui.Btn("Text from screen", delegate { App.I.StartSnip("rect", Delay(), true); }));
            delay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(100), Margin = new Padding(0, Ui.S(4), Ui.S(6), 0) };
            delay.Items.AddRange(new object[] { "No delay", "3 s delay", "5 s delay", "10 s delay" });
            delay.SelectedIndex = 0;
            bar.Controls.Add(delay);
            bar.Controls.Add(Ui.Btn("Folder", delegate { try { Process.Start("explorer.exe", "\"" + Shot.SnipFolder + "\""); } catch { } }));
            Controls.Add(bar);

            var hint = new WrapLabel
            {
                Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted,
                Text = Hotkeys.Get("snip") + " snip  ·  " + Hotkeys.Get("ocr") + " copy text from screen  ·  " + Hotkeys.Get("record") + " start / stop recording  ·  Recent snips and recordings:"
            };
            Controls.Add(hint);

            onChanged = () => { if (Live) Fill(); else stale = true; };
            Changed += onChanged;
            Ui.Theme(this);
        }

        int Delay() { return new[] { 0, 3, 5, 10 }[Math.Max(0, delay.SelectedIndex)]; }

        protected override void OnLive(bool on) { if (on && stale) Fill(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Changed -= onChanged; foreach (var t in thumbs.Values) if (t != null) t.Dispose(); thumbs.Clear(); }
            base.Dispose(disposing);
        }

        static bool IsImage(string p)
        {
            string e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp" || e == ".gif";
        }

        static Image Load(string path)
        {
            try { using (var ms = new MemoryStream(File.ReadAllBytes(path))) using (var i = Image.FromStream(ms)) return new Bitmap(i); }
            catch { return null; }
        }

        Image Thumb(string path)
        {
            Image t;
            if (thumbs.TryGetValue(path, out t)) return t;
            t = null;
            var img = Load(path);
            if (img != null)
            {
                int h = list.ItemHeight - Ui.S(18);
                int w = Math.Max(1, Math.Min(Ui.S(90), img.Width * h / Math.Max(1, img.Height)));
                t = new Bitmap(img, w, Math.Max(1, h));
                img.Dispose();
            }
            thumbs[path] = t;
            return t;
        }

        void Fill()
        {
            stale = false;
            var selected = list.SelectedItem as FileInfo;
            foreach (var thumb in thumbs.Values) if (thumb != null) thumb.Dispose();
            thumbs.Clear();
            var files = new List<FileInfo>();
            foreach (var dir in new[] { Shot.SnipFolder, Shot.VideoFolder })
            {
                try { files.AddRange(new DirectoryInfo(dir).GetFiles().Where(f => IsImage(f.FullName) || f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase))); } catch { }
            }
            var recent = files.GroupBy(f => f.FullName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderByDescending(f => f.LastWriteTime).Take(80).ToList();
            list.SetItems(recent.Cast<object>());
            if (selected != null) list.SelectedItem = recent.FirstOrDefault(f => string.Equals(f.FullName, selected.FullName, StringComparison.OrdinalIgnoreCase));
        }

        SnipEditor OpenEditor(string path)
        {
            var img = Load(path) as Bitmap;
            if (img == null) return null;
            var ed = new SnipEditor(img, path);
            ed.Show();
            return ed;
        }

        void OpenSel()
        {
            var f = list.SelectedItem as FileInfo;
            if (f == null) return;
            if (IsImage(f.FullName)) OpenEditor(f.FullName);
            else try { Process.Start(f.FullName); } catch { }
        }

        void CopySel()
        {
            var f = list.SelectedItem as FileInfo;
            if (f == null) return;
            try
            {
                if (IsImage(f.FullName)) { using (var img = Load(f.FullName)) Clipboard.SetImage(img); }
                else Clipboard.SetFileDropList(new System.Collections.Specialized.StringCollection { f.FullName });
                status("Copied.");
            }
            catch { }
        }

        void DeleteSel()
        {
            var f = list.SelectedItem as FileInfo;
            if (f == null) return;
            if (MessageBox.Show(FindForm(), "Delete " + f.Name + "?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { File.Delete(f.FullName); } catch (Exception ex) { status(ex.Message); }
            Image t; if (thumbs.TryGetValue(f.FullName, out t)) { if (t != null) t.Dispose(); thumbs.Remove(f.FullName); }
            Fill();
        }
    }
}
