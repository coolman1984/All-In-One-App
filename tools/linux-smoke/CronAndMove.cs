using System; using System.IO; using System.Reflection; using System.Linq; using System.Collections.Generic;
class T { static readonly string W = System.Environment.GetEnvironmentVariable("SMOKE_DIR");
  static void Main() {
    var asm = Assembly.LoadFrom(W + "/app.dll");
    var store = asm.GetType("StickyPro.Store");
    string dir = W + "/data2"; if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir);
    store.GetField("DirOverride").SetValue(null, dir);
    store.GetMethod("Load").Invoke(null, null);
    var cron = asm.GetType("StickyPro.CronEngine");
    var manage = cron.GetMethod("Manage");
    var args = new object[] { "create", "j1", "quiet", "every:15m", "do it", "", "folder_changed", "/nope/missing", null, null, 0 };
    manage.Invoke(null, args);
    var run = cron.GetMethod("RunDueJobs");
    var r1 = (System.Collections.IList)run.Invoke(null, null);
    var r2 = (System.Collections.IList)run.Invoke(null, null);
    Console.WriteLine("cron first tick evaluated=" + r1.Count + " second tick evaluated=" + r2.Count + " (want 1, 0)");
    File.WriteAllText(cron.GetProperty("JobsFile").GetValue(null) as string, "{broken");
    manage.Invoke(null, new object[] { "list", "", "", "", "", "", "", "", null, null, 0 });
    Console.WriteLine("broken copy kept=" + Directory.GetFiles(Path.GetDirectoryName(cron.GetProperty("JobsFile").GetValue(null) as string), "jobs.json.broken-*").Length);
    // explorer move onto itself
    string src = W + "/movesrc"; Directory.CreateDirectory(src + "/keep"); File.WriteAllText(src + "/keep/a.txt", "x");
    var api = asm.GetType("StickyPro.ApiTools");
    var res = api.GetMethod("Run").Invoke(null, new object[] { "explorer_action", new Dictionary<string, object> { { "action", "move" }, { "path", src + "/keep" }, { "dest", src } } });
    Console.WriteLine("move onto itself kept file=" + File.Exists(src + "/keep/a.txt"));
    try { api.GetMethod("Run").Invoke(null, new object[] { "explorer_action", new Dictionary<string, object> { { "action", "move" }, { "path", src }, { "dest", src + "/keep" } } }); Console.WriteLine("into-itself NOT refused"); }
    catch (TargetInvocationException e) { Console.WriteLine("into-itself refused: " + e.InnerException.Message); }
  }
}
