using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace StickyPro
{
    static class CronExecution
    {
        const int OutputLimit = 24000;

        public static bool HasAction(CronJob job)
        {
            return !string.IsNullOrWhiteSpace(job.tool) || !string.IsNullOrWhiteSpace(job.prompt) || !string.IsNullOrWhiteSpace(job.skill);
        }

        public static string Run(CronJob job, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(job.tool))
            {
                var result = AgentMemory.RunScheduledTool(job.tool, job.tool_args, cancellation);
                if (!true.Equals(result["ok"]))
                {
                    string reason = Convert.ToString(result["error"], CultureInfo.InvariantCulture);
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason) ? "The tool failed (exit " + result["exit_code"] + ")." : reason);
                }
                object output;
                return result.TryGetValue("output", out output) ? Convert.ToString(output, CultureInfo.InvariantCulture)
                    : new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(result);
            }

            string task = (job.prompt ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(job.skill))
            {
                var skill = AgentMemory.SkillManage("read", job.skill, "", "");
                string instructions = Convert.ToString(skill["content"], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(instructions)) throw new IOException("The saved skill could not be read: " + job.skill);
                task += "\n\nFollow this saved procedure (" + job.skill + "):\n" + instructions;
            }

            AgentDef provider = string.IsNullOrWhiteSpace(job.agent)
                ? AgentStore.Defs.FirstOrDefault(IsInstalled)
                : AgentStore.Def(job.agent);
            if (provider == null) throw new InvalidOperationException("No configured Assistant provider is available for this schedule.");
            if (!IsInstalled(provider)) throw new InvalidOperationException(provider.Name + " is unavailable on this PC.");
            string folder = string.IsNullOrWhiteSpace(job.folder) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : job.folder;
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Schedule folder not found: " + folder);

            var chat = new AgentChat { Id = job.last_run_id, Collection = AssistantHarness.Collection, Mode = "auto", Folder = folder };
            string prompt = AssistantHarness.WrapMessage(chat, null, "Scheduled task: " + job.name + "\n" + task);
            var run = new AgentRun();
            var outputBuffer = new StringBuilder();
            string lastError = null;
            var done = new TaskCompletionSource<int>();
            run.Event += (kind, text) =>
            {
                if (kind == "error") lastError = text;
                if (kind != "text" || string.IsNullOrWhiteSpace(text)) return;
                if (outputBuffer.Length > 0 && outputBuffer.Length < OutputLimit) outputBuffer.Append('\n');
                outputBuffer.Append(text, 0, Math.Min(text.Length, OutputLimit - outputBuffer.Length));
            };
            run.Exited += code => done.TrySetResult(code);
            cancellation.ThrowIfCancellationRequested();
            string startError;
            if (!run.Start(provider, chat, prompt, out startError)) throw new InvalidOperationException(startError);
            using (cancellation.Register(run.Stop))
            {
                if (!done.Task.Wait(300000, cancellation))
                {
                    run.Stop();
                    throw new TimeoutException("Assistant run timed out after five minutes.");
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (done.Task.Result != 0 || outputBuffer.Length == 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(lastError) ? "The Assistant returned no successful result (exit " + done.Task.Result + ")." : lastError);
            return outputBuffer.ToString();
        }

        static bool IsInstalled(AgentDef provider)
        {
            string exe, prefix;
            return provider != null && AgentRun.Resolve(provider.Exe, out exe, out prefix);
        }
    }
}
