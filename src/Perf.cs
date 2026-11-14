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
            foreach (var p in new[] { "dashboard", "notes", "prompts", "clips", "system:procs", "system:startup", "system:drives", "cards", "snip", "activity", "tagged", "tasks", "explorer", "chrome", "chrome:extensions", "system:uninstall", "power", "shortcuts", "dashboard" })
            { string page = p; steps.Add(new KeyValuePair<string, Action>("page " + page, () => App.I.ShowMain(page))); }
            int i = 0;
            var timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += delegate
            {
                if (i < steps.Count) { phase = steps[i].Key; try { steps[i].Value(); } catch (Exception ex) { App.Log("perf step: " + ex.Message); } i++; return; }
                timer.Stop(); timer.Dispose();
                ResizePages(new List<string> { "dashboard", "prompts", "system:procs" }, 0);
            };
            timer.Start();
        }

        // Resizes the window on each page in turn, then writes the report.
        static void ResizePages(List<string> pages, int index)
        {
            if (index >= pages.Count) { running = false; Ui.Later(200, Write); return; }
            string page = pages[index];
            try { App.I.ShowMain(page); } catch (Exception ex) { App.Log("perf page: " + ex.Message); }
            phase = "settle " + page;
            Ui.Later(800, () => Resize("resize " + page, () => ResizePages(pages, index + 1)));
        }

        // Live window resize from 700 to 1600 px wide and back, one step per 16 ms.
        static void Resize(string label, Action done)
        {
            var f = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (f == null) { done(); return; }
            phase = label;
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
