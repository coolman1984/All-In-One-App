using System; using System.IO; using System.Reflection; using System.Linq;
class T { static readonly string W = System.Environment.GetEnvironmentVariable("SMOKE_DIR"); static void Main() {
  var asm = Assembly.LoadFrom(W + "/app.dll"); var store = asm.GetType("StickyPro.Store");
  string dir = W + "/d3", to = W + "/d3new";
  foreach (var d in new[]{dir,to}) if (Directory.Exists(d)) Directory.Delete(d, true);
  Directory.CreateDirectory(dir); File.WriteAllText(dir + "/notes.json", "[{\"Id\":\"a\",\"Text\":\"one\"}]"); File.WriteAllText(dir + "/old.txt", "x");
  store.GetField("DirOverride").SetValue(null, dir); store.GetMethod("Load").Invoke(null, null);
  Console.WriteLine("move err=" + store.GetMethod("MoveDataTo").Invoke(null, new object[]{to}));
  System.Threading.Thread.Sleep(1100);
  var notes = (System.Collections.IList)store.GetField("Notes").GetValue(null);
  var noteT = asm.GetType("StickyPro.Note"); var n = Activator.CreateInstance(noteT); noteT.GetField("Id").SetValue(n, "b"); noteT.GetField("Text").SetValue(n, "after move"); notes.Add(n);
  store.GetMethod("MarkNotes").Invoke(null, null); store.GetMethod("SaveNow").Invoke(null, null);
  File.Delete(dir + "/old.txt");
  store.GetMethod("ReconcilePendingMove").Invoke(null, null);
  Console.WriteLine("new folder has later note=" + File.ReadAllText(to + "/notes.json").Contains("after move") + " deleted carried=" + !File.Exists(to + "/old.txt"));
}}
