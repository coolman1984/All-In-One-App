using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // Accounts page: sign in to the agent CLIs from the app, no terminal steps.
    //  - One card per CLI: who is signed in (email / plan when the CLI tells), named accounts, the account new chats use.
    //  - "Sign in" / "Add account": the CLI's own sign-in runs hidden; it opens your browser. If it prints a link or a code,
    //    the app shows it with Open / Copy; if it asks for a code, type it in the box. The card refreshes when it finishes.
    //  - Claude Code and Codex support several named accounts (each in its own folder, chosen per chat or as default).
    //  Only the CLIs' own status commands and file names are read; tokens are never read or shown.
    class AccountsPage : Panel
    {
        readonly FlowLayoutPanel cards;
        readonly Panel signin;
        readonly Label signTitle, signText;
        readonly LinkLabel signLink;
        readonly TextBox codeBox;
        readonly Button codeSend, signCancel, signCopy;
        LoginRun running;
        static readonly string[] Order = { "claude", "codex", "cline", "opencode", "agy" };

        public AccountsPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Accounts"));
            bar.Controls.Add(Ui.Btn("Refresh", delegate { Fill(); }));

            // Sign-in in progress: what the CLI says, its link or code, a box for a code it asks for.
            signin = new Panel { Dock = DockStyle.Top, Height = Ui.S(132), Visible = false, Padding = new Padding(Ui.S(16), Ui.S(10), Ui.S(16), Ui.S(10)) };
            signin.Paint += (s, e) => Ui.FillCard(e.Graphics, new Rectangle(0, 0, signin.Width - 1, signin.Height - 1), Ui.S(12), true);
            signTitle = new Label { Location = new Point(Ui.S(16), Ui.S(10)), AutoSize = true, Font = Ui.Bold, BackColor = Ui.Card };
            signText = new Label { Location = new Point(Ui.S(16), Ui.S(36)), Size = new Size(Ui.S(700), Ui.S(22)), AutoEllipsis = true, ForeColor = Ui.Muted, BackColor = Ui.Card };
            signLink = new LinkLabel { Location = new Point(Ui.S(16), Ui.S(60)), Size = new Size(Ui.S(560), Ui.S(22)), AutoEllipsis = true, BackColor = Ui.Card, LinkColor = Ui.Accent, Visible = false };
            signLink.LinkClicked += delegate { OpenUrl(signLink.Text); };
            signCopy = Ui.Btn("Copy link", delegate { App.I.CopyText(signLink.Text); });
            signCopy.Location = new Point(Ui.S(590), Ui.S(54)); signCopy.Visible = false;
            codeBox = new FluentTextBox { Location = new Point(Ui.S(16), Ui.S(92)), Width = Ui.S(320), Visible = false };
            Ui.Cue(codeBox, "Paste the code from the sign-in page");
            codeSend = Ui.Btn("Send code", delegate { SendCode(); }, true);
            codeSend.Location = new Point(Ui.S(346), Ui.S(86)); codeSend.Visible = false;
            codeBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendCode(); } };
            signCancel = Ui.Btn("Cancel", delegate { if (running != null) running.Stop(); });
            signCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right; signCancel.Location = new Point(signin.Width - Ui.S(110), Ui.S(10));
            foreach (Control c in new Control[] { signTitle, signText, signLink, signCopy, codeBox, codeSend, signCancel }) signin.Controls.Add(c);

            cards = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, Ui.S(10), Ui.S(10), Ui.S(10)) };
            cards.Resize += delegate { foreach (Control c in cards.Controls) c.Width = Math.Max(Ui.S(420), cards.ClientSize.Width - Ui.S(24)); };
            Controls.Add(cards); Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(8) }); Controls.Add(signin); Controls.Add(bar);
            Ui.Theme(this);
            signCancel.Location = new Point(Math.Max(Ui.S(600), signin.Width - Ui.S(110)), Ui.S(10));
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible && cards.Controls.Count == 0) Fill(); }

        static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }

        // ---------- cards ----------
        void Fill()
        {
            cards.SuspendLayout();
            Ui.ClearControls(cards.Controls);
            foreach (var d in AgentStore.Defs.OrderBy(x => { int i = Array.IndexOf(Order, x.Id); return i < 0 ? 99 : i; }))
                cards.Controls.Add(Card(d));
            cards.ResumeLayout();
            foreach (Control c in cards.Controls) c.Width = Math.Max(Ui.S(420), cards.ClientSize.Width - Ui.S(24));
        }

        Control Card(AgentDef d)
        {
            string file, prefix; bool installed = AgentRun.Resolve(d.Exe, out file, out prefix);
            bool named = AgentAccounts.Supported(d.Id);
            string[] names = new string[0]; if (named) try { names = AgentAccounts.Names(d.Id); } catch { }
            var card = new Panel { Height = Ui.S(78 + (named ? 34 * (names.Length + 1) : 0)), Margin = new Padding(0, 0, 0, Ui.S(12)), BackColor = Ui.Bg };
            card.Paint += (s, e) =>
            {
                Ui.FillCard(e.Graphics, new Rectangle(0, 0, card.Width - 1, card.Height - 1), Ui.S(12), false);
                Color dot; try { dot = ColorTranslator.FromHtml(d.Color ?? "#888888"); } catch { dot = Ui.Muted; }
                using (var b = new SolidBrush(dot)) { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.FillEllipse(b, Ui.S(16), Ui.S(20), Ui.S(12), Ui.S(12)); }
            };
            var name = new Label { Location = new Point(Ui.S(38), Ui.S(12)), AutoSize = true, Font = Ui.Big, Text = d.Name, BackColor = Ui.Card };
            var status = new Label { Location = new Point(Ui.S(40), Ui.S(46)), Size = new Size(Ui.S(560), Ui.S(22)), AutoEllipsis = true, BackColor = Ui.Card, ForeColor = Ui.Muted,
                Text = installed ? "Checking..." : "Not installed on this PC" };
            card.Controls.Add(name); card.Controls.Add(status);
            // One row of buttons only: a taller panel covered the first account row and hid its buttons.
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Card, Anchor = AnchorStyles.Top | AnchorStyles.Right, WrapContents = false, MaximumSize = new Size(0, Ui.S(40)) };
            card.Controls.Add(buttons);
            card.Layout += delegate
            {
                buttons.Location = new Point(card.Width - buttons.Width - Ui.S(14), Ui.S(16));
                status.Width = Math.Max(0, card.ClientSize.Width - status.Left - Ui.S(14));
            };
            if (installed)
            {
                var def = d;
                buttons.Controls.Add(Ui.Btn("Sign in", delegate { SignIn(def, ""); }, true));
                if (named) buttons.Controls.Add(Ui.Btn("Add account", delegate { AddAccount(def); }));
                if (d.Id == "codex") buttons.Controls.Add(Ui.Btn("Sign in with a code", delegate { SignIn(def, "", true); }));
                if (d.Id == "agy") buttons.Controls.Add(Ui.Btn("Sign out", delegate
                {
                    if (MessageBox.Show(FindForm(), "Sign out of Antigravity? Its saved Google sign-in is removed from Windows Credential Manager; sign in again with Sign in.", "Accounts", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                    CliAccounts.SignOutAntigravity(); Fill();
                }));
                Status(d, "", s => { if (!status.IsDisposed) { status.Text = s; status.ForeColor = s.StartsWith("Signed in") ? Ui.Success : Ui.Warning; } });
            }
            if (named && installed)
            {
                string preferred; Store.Settings.AgentDefaultAccount.TryGetValue(d.Id, out preferred); preferred = preferred ?? "";
                int y = Ui.S(80);
                card.Controls.Add(AccountRow(d, "", "Main account (" + (d.Id == "codex" ? "~\\.codex" : "~\\.claude") + ")", preferred.Length == 0, y)); y += Ui.S(34);
                foreach (var n in names) { card.Controls.Add(AccountRow(d, n, n, preferred == n, y)); y += Ui.S(34); }
            }
            return card;
        }

        // One account line: name, its status, buttons.
        Control AccountRow(AgentDef d, string account, string label, bool isDefault, int y)
        {
            var row = new Panel { Location = new Point(Ui.S(36), y), Height = Ui.S(30), BackColor = Ui.Card, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            row.Width = Ui.S(600);
            var text = new Label { Location = new Point(0, Ui.S(5)), AutoSize = true, Text = (isDefault ? "★ " : "   ") + label + (isDefault ? "   (default)" : ""), Font = isDefault ? Ui.Bold : Ui.Base, BackColor = Ui.Card };
            var st = new Label { Location = new Point(Ui.S(220), Ui.S(5)), Size = new Size(Ui.S(300), Ui.S(20)), AutoEllipsis = true, ForeColor = Ui.Muted, BackColor = Ui.Card, Text = "..." };
            row.Controls.Add(text); row.Controls.Add(st);
            var acts = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Ui.Card };
            var def = d; string acc = account;
            if (!isDefault) acts.Controls.Add(MakeDefaultTip(Small("Make default", delegate { if (acc.Length == 0) Store.Settings.AgentDefaultAccount.Remove(def.Id); else Store.Settings.AgentDefaultAccount[def.Id] = acc; Store.MarkSettings(); App.I.Status(def.Name + ": " + (acc.Length == 0 ? "main account" : acc) + " is now the default."); Fill(); })));
            acts.Controls.Add(Small("Sign in", delegate { SignIn(def, acc); }));
            acts.Controls.Add(Small("Sign out", delegate { SignOut(def, acc); }));
            if (acc.Length > 0) acts.Controls.Add(Small("Remove", delegate { Remove(def, acc); }));
            row.Controls.Add(acts);
            row.ParentChanged += delegate { if (row.Parent != null) row.Width = row.Parent.Width - Ui.S(56); };
            Status(d, account, s => { if (!st.IsDisposed) { st.Text = s; st.ForeColor = s.StartsWith("Signed in") ? Ui.Success : Ui.Muted; } });
            return row;
        }

        static readonly ToolTip defTip = new ToolTip();
        static Control MakeDefaultTip(Control c) { defTip.SetToolTip(c, "Use this account by default: new Agents chats, the Assistant and terminals opened from this app (claude / codex). No sign-out needed."); return c; }
        static Button Small(string text, EventHandler click)
        {
            var b = Ui.Btn(text, click); ((FluentButton)b).Glyph = null;
            b.Margin = new Padding(Ui.S(4), 0, 0, 0); b.MinimumSize = Ui.S(0, 26); b.Padding = new Padding(Ui.S(6), 0, Ui.S(6), 0); b.Font = Ui.Small;
            return b;
        }

        // ---------- status (the CLI's own status command, hidden; no token is read) ----------
        void Status(AgentDef d, string account, Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result;
                try
                {
                    if (d.Id == "claude")
                    {
                        string json = Run(d, account, "auth status", 20000);
                        var o = new JavaScriptSerializer().DeserializeObject(json.Substring(Math.Max(0, json.IndexOf('{')))) as Dictionary<string, object>;
                        bool on = o != null && o.ContainsKey("loggedIn") && Convert.ToBoolean(o["loggedIn"]);
                        result = on ? "Signed in" + (o.ContainsKey("email") ? " as " + o["email"] : "") + (o.ContainsKey("subscriptionType") ? " · " + o["subscriptionType"] : "") : "Not signed in";
                    }
                    else if (d.Id == "codex")
                    {
                        string s = Run(d, account, (account.Length > 0 ? "-c cli_auth_credentials_store=file " : "") + "login status", 20000).Trim();
                        result = s.IndexOf("Logged in", StringComparison.OrdinalIgnoreCase) >= 0 ? "Signed in" + (s.Contains("ChatGPT") ? " with ChatGPT" : s.Contains("API") ? " with an API key" : "") : "Not signed in";
                    }
                    else if (d.Id == "cline") result = CliAccounts.ClineStatus(d.Exe);
                    else result = CliAccounts.Status(d.Id);
                }
                catch (Exception ex) { result = "Could not check (" + ex.Message + ")"; }
                try { if (!IsDisposed && IsHandleCreated) BeginInvoke(done, result); } catch { }
            });
        }

        static ProcessStartInfo Start(AgentDef d, string account, string args)
        {
            string file, prefix;
            if (!AgentRun.Resolve(d.Exe, out file, out prefix)) throw new IOException(d.Name + " is not installed.");
            bool viaCmd = file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && prefix.StartsWith("/d");
            var psi = new ProcessStartInfo(file, prefix + args + (viaCmd ? "\"" : ""))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            if (account.Length > 0) AgentAccounts.Configure(psi, d.Id, account, Path.GetDirectoryName(AgentAccounts.Home(d.Id, account)), true);
            return psi;
        }

        static string Run(AgentDef d, string account, string args, int ms)
        {
            using (var p = Process.Start(Start(d, account, args)))
            {
                p.StandardInput.Close();
                var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } return ""; }
                return o.Result + "\n" + e.Result;
            }
        }

        // ---------- sign in / out ----------
        void AddAccount(AgentDef d)
        {
            string name = Ui.Ask(d.Name + ": add an account", "A short name for it (letters, numbers, - or _), e.g. work or personal", "", FindForm());
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim().ToLowerInvariant();
            if (!AgentAccounts.Valid(name)) { MessageBox.Show(FindForm(), "Use letters, numbers, - or _ (up to 24).", "Accounts"); return; }
            SignIn(d, name);
        }

        void SignIn(AgentDef d, string account, bool device = false)
        {
            if (running != null) running.Stop();
            string args;
            switch (d.Id)
            {
                case "claude": args = "auth login"; break;
                case "codex": args = (account.Length > 0 ? "-c cli_auth_credentials_store=file " : "") + "login" + (device ? " --device-auth" : ""); break;
                default:
                    // Cline / OpenCode / others ask questions in their own menu: a small terminal window is the honest way.
                    CliAccounts.SignInDefault(d);
                    return;
            }
            ShowSignin(d.Name + (account.Length > 0 ? " · " + account : "") + ": signing in", "Your browser opens the sign-in page. Finish there; this page updates by itself.");
            // Claude asks "Paste code here if prompted >" without ending the line, so the prompt can not be detected:
            // the code box is always there during a Claude sign-in.
            if (d.Id == "claude") { codeBox.Visible = codeSend.Visible = true; signText.Text = "Your browser opens the sign-in page. If it shows a code, paste it below and press Send code."; }
            try
            {
                running = new LoginRun(Start(d, account, args), this);
                running.Line += OnLine;
                running.Exited += code =>
                {
                    running = null;
                    if (code == 0) { if (account.Length > 0 && !Store.Settings.AgentDefaultAccount.ContainsKey(d.Id)) { Store.Settings.AgentDefaultAccount[d.Id] = account; Store.MarkSettings(); } signin.Visible = false; App.I.Status(d.Name + ": signed in."); }
                    else { signTitle.Text = d.Name + ": sign-in did not finish"; signText.Text = "Code " + code + ". Try again, or use a code if the browser page could not reach this PC."; codeBox.Visible = codeSend.Visible = false; }
                    Fill();
                };
                running.Start();
            }
            catch (Exception ex) { signTitle.Text = d.Name + ": could not start the sign-in"; signText.Text = ex.Message; }
        }

        void ShowSignin(string title, string text)
        {
            signTitle.Text = title; signText.Text = text;
            signLink.Visible = signCopy.Visible = codeBox.Visible = codeSend.Visible = false;
            signin.Visible = true; signCancel.Location = new Point(Math.Max(Ui.S(600), signin.Width - Ui.S(110)), Ui.S(10));
        }

        static readonly Regex Url = new Regex(@"https?://[^\s""'<>]+");
        static readonly Regex DeviceCode = new Regex(@"\b([A-Z0-9]{4,5}-[A-Z0-9]{4,5})\b");
        void OnLine(string line)
        {
            var u = Url.Match(line);
            if (u.Success && !u.Value.Contains("localhost") && !u.Value.Contains("127.0.0.1")) { signLink.Text = u.Value.TrimEnd('.', ','); signLink.Visible = signCopy.Visible = true; }
            var c = DeviceCode.Match(line);
            if (c.Success) { signText.Text = "Your code: " + c.Groups[1].Value + "   (open the link, type this code there)"; App.I.CopyText(c.Groups[1].Value); }
            else if (Regex.IsMatch(line, @"(paste|enter|input).{0,20}code", RegexOptions.IgnoreCase)) { signText.Text = "The sign-in page shows a code: paste it below."; codeBox.Visible = codeSend.Visible = true; codeBox.Focus(); }
            else if (line.Trim().Length > 0 && !u.Success && line.Length < 200) signText.Text = line.Trim();
        }

        void SendCode()
        {
            if (running == null || codeBox.Text.Trim().Length == 0) return;
            running.Send(codeBox.Text.Trim()); codeBox.Clear(); signText.Text = "Code sent. Finishing...";
        }

        void SignOut(AgentDef d, string account)
        {
            if (MessageBox.Show(FindForm(), "Sign out of " + d.Name + (account.Length > 0 ? " (" + account + ")" : "") + "?", "Accounts", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            string args = d.Id == "claude" ? "auth logout" : (account.Length > 0 ? "-c cli_auth_credentials_store=file " : "") + "logout";
            ThreadPool.QueueUserWorkItem(delegate { try { Run(d, account, args, 20000); } catch { } try { BeginInvoke((Action)Fill); } catch { } });
        }

        void Remove(AgentDef d, string account)
        {
            if (MessageBox.Show(FindForm(), "Remove the account \"" + account + "\" from this PC? Its sign-in for " + d.Name + " is deleted; your chats stay.", "Accounts", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            try
            {
                string home = AgentAccounts.Home(d.Id, account);
                if (Directory.Exists(home) && home.IndexOf(".herdr-accounts", StringComparison.OrdinalIgnoreCase) >= 0) Directory.Delete(home, true);
                string pref; if (Store.Settings.AgentDefaultAccount.TryGetValue(d.Id, out pref) && pref == account) { Store.Settings.AgentDefaultAccount.Remove(d.Id); Store.MarkSettings(); }
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Could not remove it: " + ex.Message, "Accounts"); }
            Fill();
        }
    }

    // A CLI sign-in running hidden: its lines come to the page; a code can be sent to it.
    class LoginRun
    {
        readonly ProcessStartInfo psi;
        readonly Control ui;
        Process p;
        public event Action<string> Line;
        public event Action<int> Exited;
        public LoginRun(ProcessStartInfo psi, Control ui) { this.psi = psi; this.ui = ui; }
        void OnUi(Action a) { try { if (!ui.IsDisposed) ui.BeginInvoke(a); } catch { } }

        public void Start()
        {
            p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (s, e) => Raise(e.Data);
            p.ErrorDataReceived += (s, e) => Raise(e.Data);
            p.Exited += delegate { int code = -1; try { p.WaitForExit(); code = p.ExitCode; } catch { } OnUi(() => { var h = Exited; if (h != null) h(code); }); };
            p.Start();
            ConsoleGuard.Watch(p);
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
        }

        void Raise(string line)
        {
            if (line == null) return;
            line = Regex.Replace(line, @"\x1B\[[0-9;?]*[A-Za-z]", "");   // colour codes
            OnUi(() => { var h = Line; if (h != null) h(line); });
        }

        public void Send(string text) { try { p.StandardInput.WriteLine(text); p.StandardInput.Flush(); } catch { } }
        public void Stop() { try { if (p != null && !p.HasExited) p.Kill(); } catch { } }
    }
}
