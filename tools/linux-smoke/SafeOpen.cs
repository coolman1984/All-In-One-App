using System; using System.IO; using System.Reflection; using System.Linq;
class T { static readonly string W = System.Environment.GetEnvironmentVariable("SMOKE_DIR");
  static void Main() {
    var asm = Assembly.LoadFrom(W + "/app.dll");
    var store = asm.GetType("StickyPro.Store");
    string dir = W + "/data"; if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir);
    File.WriteAllText(dir + "/notes.json", "[{\"Id\":\"a\",\"Text\":\"old1\"},{\"Id\":\"b\",\"Text\":\"old2\"}]");
    File.WriteAllText(dir + "/notes.json.bak", "[{\"Id\":\"a\",\"Text\":\"older\"}]");
    store.GetField("DirOverride").SetValue(null, dir);
    var hold = new FileStream(dir + "/notes.json", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    store.GetMethod("Load").Invoke(null, null);
    Console.WriteLine("load ms=" + sw.ElapsedMilliseconds);
    var locked = (string[])store.GetMethod("LockedSections").Invoke(null, null);
    Console.WriteLine("locked=" + string.Join(",", locked));
    var notes = (System.Collections.IList)store.GetField("Notes").GetValue(null);
    Console.WriteLine("notes loaded (from bak, display only)=" + notes.Count);
    // add a note in session, then exit-style save
    var noteT = asm.GetType("StickyPro.Note"); var n = Activator.CreateInstance(noteT); noteT.GetField("Id").SetValue(n, "c"); noteT.GetField("Text").SetValue(n, "new session"); notes.Add(n);
    store.GetMethod("MarkNotes").Invoke(null, null);
    store.GetMethod("SaveNow").Invoke(null, null);
    hold.Dispose();
    Console.WriteLine("primary untouched=" + File.ReadAllText(dir + "/notes.json").Contains("old2"));
    Console.WriteLine("files=" + string.Join(" ", Directory.GetFiles(dir).Select(Path.GetFileName)));
    Console.WriteLine(store.GetMethod("RetryLocked").Invoke(null, null));
    Console.WriteLine("notes after merge=" + notes.Count);
    store.GetMethod("SaveNow").Invoke(null, null);
    string txt = File.ReadAllText(dir + "/notes.json");
    Console.WriteLine("saved has old2+new: " + (txt.Contains("old2") && txt.Contains("new session")) + " snapshots=" + Directory.GetDirectories(dir + "/snapshots").Length);
    // shrink guard
    File.WriteAllText(dir + "/clips.json", "[" + string.Join(",", Enumerable.Range(0,200).Select(i => "{\"Id\":\"x" + i + "\",\"Text\":\"" + new string('z',50) + "\"}")) + "]");
  }
}
