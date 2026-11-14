# Part 1 - Smoothness (target: no UI-thread stall over 16 ms in normal use = 60+ FPS)

Read `tools/PLAN-allinone.md` first (hard rules). Folder: `D:\WORK\Software Development\GitHub\Sticky note`.
Do the tasks in order. After each task: `pwsh -NoProfile -File tools\test-build.ps1` -> `BUILD OK`, then commit.

---------------------------------------------------------------------------------------------------------------
## Task 1.1 - Process sampling off the UI thread (src/SysInfo.cs, class ProcessSampler)

Today `Sample()` runs on the UI thread every 2 s and calls `SampleCore()`, which walks ~300 processes and calls
`ProcMeta.Fill` (OpenProcess + FileVersionInfo per new process: can take 100-500 ms on the first sample).

Change it so the heavy work runs on a worker thread and only the results are published on the UI thread.

1. Add fields to `ProcessSampler`:
   ```csharp
   static readonly object sampleLock = new object();
   static bool sampling;              // UI thread only
   ```
2. Rename the existing `SampleCore()` body so it RETURNS its results instead of writing the public statics.
   Create a small private class inside ProcessSampler:
   ```csharp
   class SampleResult { public List<ProcInfo> List; public double Cpu; public int MemLoad; public ulong MemTotal, MemAvail; public bool Ok; }
   ```
   `static SampleResult SampleCore()` builds a `SampleResult`:
   - keep the exact NtQuerySystemInformation parsing code and offsets unchanged;
   - where it now assigns `CpuTotal = ...` assign `r.Cpu = ...` instead, but initialise `r.Cpu = CpuTotal` first
     (so a failed GetSystemTimes keeps the last value);
   - `r.MemLoad/MemTotal/MemAvail` likewise (initialise from the current statics);
   - call `ProcMeta.Fill(list)` there (it is already thread-safe for this use: `byPid` is only touched by Fill;
     `Describe` locks `descByPath`);
   - set `r.List = list; r.Ok = true;` at the end; on `status != 0` or `IntPtr.Size != 8` return `r` with Ok=false.
   - The whole body runs inside `lock (sampleLock) { ... }` (protects `buf`, `bufLen`, `prev`, `prevStamp`,
     `prevIdle`, `prevKernel`, `prevUser`).
3. Replace `public static void Sample()` with:
   ```csharp
   public static void Sample()
   {
       if (sampling) return;              // one sample at a time
       sampling = true;
       System.Threading.ThreadPool.QueueUserWorkItem(delegate
       {
           SampleResult r = null;
           try { r = SampleCore(); } catch (Exception ex) { App.Log("Process sample failed: " + ex.Message); }
           try { App.I.Sync.BeginInvoke(new Action(() => Publish(r))); }
           catch { sampling = false; }
       });
   }

   static void Publish(SampleResult r)
   {
       sampling = false;
       if (r != null && r.Ok)
       {
           Last = r.List; CpuTotal = r.Cpu; MemLoad = r.MemLoad; MemTotal = r.MemTotal; MemAvail = r.MemAvail;
       }
       PushHistory(CpuHistory, CpuTotal); PushHistory(RamHistory, MemLoad);
       var h = Updated; if (h != null) h();
   }
   ```
   (`sampling` is only read/written on the UI thread: Sample() is called from the UI timer, from `Watch`, and from
   `Ui.Later(250, ProcessSampler.Sample)` in SystemPages.cs line ~333 - all UI thread. Keep those callers.)
4. In `Watch(...)`, where it stops the timer it does `prev.Clear(); prevStamp = 0;` - wrap that in
   `lock (sampleLock) { prev.Clear(); prevStamp = 0; }`.
5. Check every other method in ProcessSampler that touches `prev`, `buf` or `Last` (e.g. end-task code) still
   compiles and behaves; reading `Last` on the UI thread is fine because `Publish` replaces the list reference.

## Task 1.2 - Image clips encoded off the UI thread (src/App.cs CaptureClipboard, src/Data.cs MakeImageClip)

Today copying a screenshot runs `img.Save(png) + SHA1 + File.WriteAllBytes` on the UI thread (100-400 ms for a
large image).

1. In src/Data.cs split `MakeImageClip(Image img)` in two, keep the old method working by calling the new ones:
   ```csharp
   // Worker-thread safe: no access to Clips. Returns png bytes and hash.
   public static byte[] EncodePng(Image img, out string hash)
   {
       using (var ms = new MemoryStream())
       {
           img.Save(ms, ImageFormat.Png);
           byte[] bytes = ms.ToArray();
           using (var sha = SHA1.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
           return bytes;
       }
   }

   // UI thread: builds the clip; writes the file only if this image is new.
   public static Clip ImageClipFrom(byte[] bytes, string hash, int w, int h)
   {
       var existing = Clips.FirstOrDefault(x => x.Kind == "image" && x.Hash == hash);
       var c = new Clip { Id = NewId(), Kind = "image", Hash = hash, ImgW = w, ImgH = h, Text = "Image " + w + " x " + h };
       if (existing != null) c.Image = existing.Image;
       else { c.Image = c.Id + ".png"; File.WriteAllBytes(Path.Combine(ImgDir, c.Image), bytes); }
       return c;
   }

   public static Clip MakeImageClip(Image img)
   {
       string hash; var bytes = EncodePng(img, out hash);
       return ImageClipFrom(bytes, hash, img.Width, img.Height);
   }
   ```
   (Writing the PNG file on the UI thread is acceptable here only if it is new; to keep it fully off the UI thread
   write the file in the worker BEFORE switching back, see step 2, and pass `alreadyWritten` - see below.)
   Better version (do this): give `ImageClipFrom` a 5th parameter `string writtenName` - when not null the worker
   already wrote the file `writtenName` into `ImgDir`; then: if an existing clip has the same hash, delete
   `writtenName` (try/catch) and reuse `existing.Image`; else `c.Image = writtenName` (use `c.Id + ".png"` as
   the name: generate the Id in the worker with `NewId()` - it is just a Guid and thread-safe).
2. In src/App.cs `CaptureClipboard()`, refactor the tail (from `if (c == null) return;` to the end of the try)
   into a new method `void AddCaptured(Clip c, string source)` that sets `Id` if null, `Time = Store.Now()`,
   `Source = source`, calls `Store.AddClip(c)` and the existing paste-queue code. Then the image branch becomes:
   ```csharp
   if (c == null && d.GetDataPresent(DataFormats.Bitmap))
   {
       var img = d.GetData(DataFormats.Bitmap) as Image;
       if (img != null)
       {
           Bitmap copy;
           using (img) copy = new Bitmap(img);          // fast copy, releases the clipboard object now
           string source = pendingSource;
           System.Threading.ThreadPool.QueueUserWorkItem(delegate
           {
               try
               {
                   string hash, id = Store.NewId(), name = id + ".png";
                   byte[] bytes; int w, h;
                   using (copy) { bytes = Store.EncodePng(copy, out hash); w = copy.Width; h = copy.Height; }
                   File.WriteAllBytes(Path.Combine(Store.ImgDir, name), bytes);
                   Sync.BeginInvoke(new Action(() =>
                   {
                       try { var clip = Store.ImageClipFrom(bytes, hash, w, h, name); clip.Id = id; AddCaptured(clip, source); }
                       catch (Exception ex) { Log("Capture failed: " + ex.Message); }
                   }));
               }
               catch (Exception ex) { Log("Image capture failed: " + ex.Message); }
           });
           return;
       }
   }
   ```
   Keep text and file-drop clips synchronous (they are cheap). Make sure `using System.IO;` exists in App.cs.
   `Store.NewId()` must be callable from a worker: it is `Guid.NewGuid().ToString("N")` - fine.

## Task 1.3 - Double-buffer every container (src/Ui.cs)

Panels, FlowLayoutPanels and TableLayoutPanels are not double-buffered by default and flicker while resizing.
1. In `static class Ui` add:
   ```csharp
   static readonly System.Reflection.PropertyInfo doubleBufferedProp =
       typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
   // Flicker-free resizing: containers paint off-screen. (Panel covers FlowLayoutPanel and TableLayoutPanel.)
   public static void Smooth(Control c)
   {
       if (doubleBufferedProp == null) return;
       if (c is Panel || c is UserControl || c is TabPage || c is SplitContainer || c is Form)
           try { doubleBufferedProp.SetValue(c, true, null); } catch { }
   }
   ```
2. In `Ui.Theme(Control root)`: call `Smooth(root);` once at the top, and `Smooth(c);` for every child inside the
   existing `foreach (Control c in root.Controls)` loop (before `if (c.HasChildren) Theme(c);`).
   Do NOT set the WS_EX_COMPOSITED style anywhere (it makes WinForms sluggish).

## Task 1.4 - Page switch without partial repaints (src/MainForm.cs, src/Native.cs)

1. In src/Native.cs add (if not present) `public const int WM_SETREDRAW = 0x000B;` (SendMessage with
   IntPtr overload already exists - check the exact signature in Native.cs and use it).
2. In src/Ui.cs add:
   ```csharp
   // Applies several visual changes as one repaint.
   public static void Batch(Control c, Action change)
   {
       if (c == null || !c.IsHandleCreated || !c.Visible) { change(); return; }
       Native.SendMessage(c.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
       try { c.SuspendLayout(); try { change(); } finally { c.ResumeLayout(true); } }
       finally { Native.SendMessage(c.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero); c.Invalidate(true); }
   }
   ```
3. In MainForm: the constructor has `var content = new Panel();` - promote it to a field `readonly Panel content;`
   (assign `content = new Panel();`). In `ShowPage(string p)` wrap everything from `page = p;` down to (not
   including) `RefreshCurrent(); FocusSearch();` in `Ui.Batch(content, () => { ... });` - also the nav `Selected`
   assignments may stay inside. `RefreshCurrent()` and `FocusSearch()` run after the batch.
   Note: `ShowPage` is called from the constructor before the handle exists - `Batch` handles that.

## Task 1.5 - Accessories at 60 FPS without wasted work (src/Accessories.cs)

1. `MagnifierForm`: `timer.Interval = 16`. Keep the existing "skip when the cursor did not move" check, but also
   refresh at least every 100 ms so moving content under a still cursor updates: add `int idleTicks;` - when the
   cursor did not move, `if (++idleTicks < 6 && buffer != null) return; idleTicks = 0;` else reset `idleTicks = 0`.
2. `PixelRuler`: `timer.Interval = 16` (it already repaints only when the cursor moved).
3. `CrossHairOverlay` and `ProtractorOverlay` currently `Invalidate()` the WHOLE virtual screen on every mouse
   move (slow on 4K / two monitors). Make them invalidate only what changed:
   - In `FrozenOverlay` add `protected Rectangle LastLabel;` and make the static `Label(...)` helper an instance
     method that stores the rectangle it painted into `LastLabel` (inflate by 2 px).
   - `CrossHairOverlay`: override `OnMouseMove`: remember old point `o = Mouse`, set `Mouse = e.Location`, then
     invalidate: vertical strip `new Rectangle(o.X - 2, 0, 5, Height)` and the same for the new X; horizontal
     strip `new Rectangle(0, o.Y - 2, Width, 5)` and the new Y; the old `LastLabel` and a generous rect around the
     new mouse position for the new label (`new Rectangle(e.X - Ui.S(320), e.Y - Ui.S(120), Ui.S(640), Ui.S(240))`);
     when `reference` is set also invalidate the 4 edges of the old and new dashed rectangles (each edge as a
     5-px-thick strip) and the bounding box of the old and new reference->mouse line
     (`Rectangle.FromLTRB(min x, min y, max x, max y)` inflated by 3). Do not call base.OnMouseMove's full
     Invalidate (override fully; do not call `base.OnMouseMove` of FrozenOverlay - call `Control`'s by not calling
     base at all is fine for these overlays).
   - `ProtractorOverlay`: invalidate the bounding box of vertex, armA, armB (old and new) inflated by
     `Ui.S(100)` plus the old `LastLabel` and the new label area (same generous rect as above) and the help bar
     `new Rectangle(0, 0, Ui.S(900), Ui.S(50))`.
   - `ColorPickerOverlay` already invalidates locally - leave it.
   - `WhiteBoardOverlay` already invalidates locally - leave it.

## Task 1.6 - Built-in smoothness probe (new file src/Perf.cs, src/Program.cs)

A self-test the lead runs to PROVE 60 FPS: it measures how late the UI thread answers while the app switches
pages and resizes.

1. New file src/Perf.cs:
   ```csharp
   using System;
   using System.Collections.Generic;
   using System.Diagnostics;
   using System.Drawing;
   using System.IO;
   using System.Linq;
   using System.Text;
   using System.Threading;
   using System.Windows.Forms;

   namespace StickyPro
   {
       // "--perf": scripted tour of the main window while a worker thread pings the UI thread every 4 ms.
       // A ping answered later than 16.7 ms = a dropped frame at 60 FPS. Report: data\perf.txt
       static class Perf
       {
           static volatile bool running;
           static volatile string phase = "start";
           static readonly object gate = new object();
           static readonly Dictionary<string, List<double>> samples = new Dictionary<string, List<double>>();

           public static void Run()
           {
               running = true;
               var t = new Thread(Pinger) { IsBackground = true, Name = "perf-pinger" };
               t.Start();
               var steps = new List<KeyValuePair<string, Action>>();
               steps.Add(new KeyValuePair<string, Action>("open main", () => App.I.ShowMain("dashboard")));
               foreach (var p in new[] { "dashboard", "notes", "prompts", "clips", "system:procs", "system:startup", "system:drives", "cards", "snip", "activity", "tagged", "dashboard" })
               { string page = p; steps.Add(new KeyValuePair<string, Action>("page " + page, () => App.I.ShowMain(page))); }
               int i = 0;
               var timer = new System.Windows.Forms.Timer { Interval = 1500 };
               timer.Tick += delegate
               {
                   if (i < steps.Count) { phase = steps[i].Key; try { steps[i].Value(); } catch (Exception ex) { App.Log("perf step: " + ex.Message); } i++; return; }
                   timer.Stop(); timer.Dispose();
                   Resize(() => { running = false; Ui.Later(200, Write); });
               };
               timer.Start();
           }

           // Live window resize from 700 to 1600 px wide and back, one step per 16 ms.
           static void Resize(Action done)
           {
               var f = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
               if (f == null) { done(); return; }
               phase = "resize";
               var widths = new List<int>();
               for (int w = 700; w <= 1600; w += 20) widths.Add(Ui.S(w));
               for (int w = 1600; w >= 700; w -= 20) widths.Add(Ui.S(w));
               int k = 0;
               var t = new System.Windows.Forms.Timer { Interval = 16 };
               t.Tick += delegate
               {
                   if (k >= widths.Count || f.IsDisposed) { t.Stop(); t.Dispose(); done(); return; }
                   f.Width = widths[k++];
               };
               t.Start();
           }

           static void Pinger()
           {
               var answered = new AutoResetEvent(false);
               while (running)
               {
                   long start = Stopwatch.GetTimestamp();
                   string ph = phase;
                   try
                   {
                       App.I.Sync.BeginInvoke(new Action(() =>
                       {
                           double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                           lock (gate) { List<double> l; if (!samples.TryGetValue(ph, out l)) samples[ph] = l = new List<double>(); l.Add(ms); }
                           answered.Set();
                       }));
                   }
                   catch { return; }
                   answered.WaitOne(2000);
                   Thread.Sleep(4);
               }
           }

           static void Write()
           {
               var sb = new StringBuilder();
               sb.AppendLine("All in One smoothness probe " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
               sb.AppendLine("phase | pings | worst ms | p95 ms | stalls > 16.7 ms");
               double worst = 0; int stalls = 0;
               lock (gate)
               {
                   foreach (var kv in samples)
                   {
                       var l = kv.Value.OrderBy(x => x).ToList();
                       if (l.Count == 0) continue;
                       double p95 = l[Math.Min(l.Count - 1, (int)(l.Count * 0.95))];
                       int s = l.Count(x => x > 16.7);
                       worst = Math.Max(worst, l[l.Count - 1]); stalls += s;
                       sb.AppendLine(kv.Key + " | " + l.Count + " | " + l[l.Count - 1].ToString("0.0") + " | " + p95.ToString("0.0") + " | " + s);
                   }
               }
               sb.AppendLine("TOTAL worst " + worst.ToString("0.0") + " ms, stalls " + stalls);
               try { File.WriteAllText(Path.Combine(Store.Dir, "perf.txt"), sb.ToString()); } catch (Exception ex) { App.Log("perf write: " + ex.Message); }
           }
       }
   }
   ```
2. src/Program.cs: parse `--perf` like the other flags (`bool perf = Array.Exists(args, a => a.ToLowerInvariant() == "--perf");`)
   and after `var app = new App(startup);` (and after the `--page` handling) add
   `if (perf) Ui.Later(2500, Perf.Run);`.
   Do not run it yourself (rule 3) - the lead runs it.

## Task 1.7 - Quick self-review of part 1
Re-read every diff you made (`git diff HEAD~6`), check: C# 5 only, every Bitmap/Font/Brush disposed, no UI access
from worker threads (only `App.I.Sync.BeginInvoke` / `Sync.BeginInvoke`), `BUILD OK`, no warnings. Fix anything.
Then give the final answer in the format of PLAN rule 10.
