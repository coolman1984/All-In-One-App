using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // One "Send to" menu for the whole app, so every page can hand a file, folder or text to every other page:
    // Explorer, Terminals, Agents, Browser, Markdown, Sticky notes, Tasks, Prompts, Tagged files, the clipboard.
    static class AppLinks
    {
        static readonly string[] WebFiles = { ".html", ".htm", ".pdf", ".eml", ".msg", ".mysingle", ".mht", ".mhtml", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".txt", ".json", ".xml" };
        static readonly string[] MdFiles = { ".md", ".markdown", ".mdx", ".mdown" };

        static bool IsFolder(string p) { try { return Directory.Exists(p); } catch { return false; } }
        static bool IsFile(string p) { try { return File.Exists(p); } catch { return false; } }
        static string Ext(string p) { try { return (Path.GetExtension(p) ?? "").ToLowerInvariant(); } catch { return ""; } }
        static string FolderOf(string p) { return IsFolder(p) ? p : IsFile(p) ? Path.GetDirectoryName(p) : null; }

        // Actions for a file or folder (Open, Show in folder, Send to ...).
        public static void AddPathItems(ToolStripItemCollection items, string path, IWin32Window owner)
        {
            if (string.IsNullOrEmpty(path)) return;
            string folder = FolderOf(path);
            if (IsFile(path)) items.Add("Open", null, delegate { Run(path); });
            if (folder != null)
            {
                items.Add("Open its folder in the app (Explorer page)", null, delegate { App.I.OpenInExplorerPage(folder); });
                items.Add("Show in Windows Explorer", null, delegate { try { Process.Start("explorer.exe", IsFile(path) ? "/select,\"" + path + "\"" : "\"" + path + "\""); } catch { } });
            }
            items.Add(SendTo(path, null, owner));
            items.Add("Copy path", null, delegate { App.I.CopyText(path); });
        }

        // "Send to" submenu. path: a file or folder (or null); text: text to hand over (or null).
        public static ToolStripMenuItem SendTo(string path, string text, IWin32Window owner)
        {
            var m = new ToolStripMenuItem("Send to");
            string folder = path != null ? FolderOf(path) : null;
            if (folder != null)
            {
                m.DropDownItems.Add("Explorer page", null, delegate { App.I.OpenInExplorerPage(folder); });
                m.DropDownItems.Add("Terminal (PowerShell here)", null, delegate { Term.Open(folder, ""); });
                m.DropDownItems.Add("Agents (new chat in this folder)", null, delegate { App.I.AddAgentFolder(folder); });
                m.DropDownItems.Add("Terminals page (add as a project folder)", null, delegate { App.I.AddTerminalFolder(folder); });
            }
            if (path != null && IsFile(path))
            {
                if (WebFiles.Contains(Ext(path))) m.DropDownItems.Add("Browser (open the file in a tab)", null, delegate { App.I.OpenInBrowser(path); });
            if (DocumentsPage.Handles(path)) m.DropDownItems.Add("Documents (view it in the app, read only)", null, delegate { App.I.OpenDocument(path); });
                if (MdFiles.Contains(Ext(path))) m.DropDownItems.Add("Markdown notes (read it)", null, delegate { App.I.OpenMarkdown(path); });
                m.DropDownItems.Add("Agents (attach the file to a message)", null, delegate { App.I.SendToAgents("Please look at this file: " + path); });
            }
            if (path != null) m.DropDownItems.Add("Tagged files (tag it)...", null, delegate { TaggedPanel.AskTag(new List<string> { path }, owner); });
            string body = text ?? path;
            if (!string.IsNullOrEmpty(body))
            {
                if (m.DropDownItems.Count > 0) m.DropDownItems.Add(new ToolStripSeparator());
                if (text != null) m.DropDownItems.Add("Agents (into the message box)", null, delegate { App.I.SendToAgents(text); });
                m.DropDownItems.Add("Sticky note (new)", null, delegate { App.I.NewNote(null, body); });
                m.DropDownItems.Add("Task (new)", null, delegate { var t = Store.NewTask(Ui.OneLine(body, 120)); if (body.Length > 120) { t.Notes = body; Store.TouchTask(t); } App.I.ShowMain("tasks"); });
                m.DropDownItems.Add("Prompt (new)", null, delegate { Store.NewPrompt(Store.TitleFrom(body), body, ""); App.I.ShowMain("prompts"); });
                if (text != null) m.DropDownItems.Add("Browser (search the web)", null, delegate { App.I.OpenInBrowser("https://www.google.com/search?q=" + Uri.EscapeDataString(Ui.OneLine(text, 300))); });
                m.DropDownItems.Add("Clipboard (copy)", null, delegate { App.I.CopyText(body); });
            }
            Ui.ThemeMenu(m.DropDown);
            return m;
        }

        // "Send to" for menus built once and shown many times: filled when it opens, for the current selection.
        public static ToolStripMenuItem LazySendTo(Func<string> path, Func<string> text, IWin32Window owner)
        {
            var m = new ToolStripMenuItem("Send to");
            m.DropDownItems.Add("...");
            m.DropDownOpening += delegate
            {
                m.DropDownItems.Clear();
                string p = null, x = null;
                try { p = path != null ? path() : null; x = text != null ? text() : null; } catch { }
                var built = SendTo(string.IsNullOrEmpty(p) ? null : p, string.IsNullOrEmpty(x) ? null : x, owner);
                while (built.DropDownItems.Count > 0) { var it = built.DropDownItems[0]; built.DropDownItems.RemoveAt(0); m.DropDownItems.Add(it); }
                if (m.DropDownItems.Count == 0) m.DropDownItems.Add(new ToolStripMenuItem("(nothing selected)") { Enabled = false });
                Ui.ThemeMenu(m.DropDown);
            };
            return m;
        }

        static void Run(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Could not open: " + ex.Message, App.Name); }
        }
    }
}
