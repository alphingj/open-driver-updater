// Code-only WPF UI (no XAML: builds with in-box csc). Apple-inspired theme,
// follows the Windows app light/dark setting. C# 5 syntax (Framework csc).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace OduApp
{
    internal static class Pal
    {
        public static bool Dark;
        public static Color Page() { return Dark ? C(29, 29, 31) : C(245, 245, 247); }
        public static Color Side() { return Dark ? C(28, 28, 30) : C(251, 251, 253); }
        public static Color CardBg() { return Dark ? C(44, 44, 46) : C(255, 255, 255); }
        public static Color Ink() { return Dark ? C(245, 245, 247) : C(29, 29, 31); }
        public static Color Muted() { return Dark ? C(161, 161, 166) : C(110, 110, 115); }
        public static Color Line() { return Dark ? C(72, 72, 74) : C(229, 229, 234); }
        public static Color Pill() { return Dark ? C(72, 72, 74) : C(222, 222, 227); }
        public static Color Blue() { return Color.FromRgb(0, 113, 227); }
        private static Color C(byte r, byte g, byte b) { return Color.FromRgb(r, g, b); }

        public static void Detect()
        {
            try
            {
                object v = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                Dark = (v is int) && ((int)v == 0);
            }
            catch { Dark = false; }
        }
    }

    public class MainWindow : Window
    {
        private List<Device> _devices = new List<Device>();
        private List<DriverUpdate> _updates = new List<DriverUpdate>();
        private bool _live;
        private StackPanel _content;
        private TextBlock _status;
        private TextBlock _pill;

        public MainWindow()
        {
            Pal.Detect();
            Title = "Open Driver Updater " + AppVersion.Value;
            Width = 980;
            Height = 640;
            Background = new SolidColorBrush(Pal.Page());
            FontFamily = new FontFamily("Segoe UI");
            Build();
            Refresh(false);
        }

        private void Build()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var side = new StackPanel { Background = new SolidColorBrush(Pal.Side()) };
            side.Children.Add(Brand());
            string[] views = new string[] { "Overview", "Updates", "Devices", "Backup", "Schedule", "Excluded", "Components", "Win Update" };
            foreach (string v in views)
            {
                var b = NavButton(v);
                if (v == "Updates")
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    sp.Children.Add(new TextBlock
                    {
                        Text = v, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Pal.Ink())
                    });
                    _pill = new TextBlock
                    {
                        Text = "0", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1),
                        Background = new SolidColorBrush(Pal.Pill()),
                        Foreground = new SolidColorBrush(Pal.Ink())
                    };
                    sp.Children.Add(_pill);
                    b.Content = sp;
                }
                else b.Content = v;
                string name = v;
                b.Click += delegate { Show(name); };
                side.Children.Add(b);
            }
            Grid.SetColumn(side, 0);
            root.Children.Add(side);

            var right = new DockPanel { Margin = new Thickness(24) };
            var top = new DockPanel();
            var title = new TextBlock
            {
                Text = "Open Driver Updater", FontSize = 26, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Pal.Ink())
            };
            DockPanel.SetDock(title, Dock.Left);
            top.Children.Add(title);
            var scan = BlueButton("Scan now");
            scan.Click += delegate { Refresh(_live); };
            DockPanel.SetDock(scan, Dock.Right);
            top.Children.Add(scan);
            var live = new CheckBox
            {
                Content = "Live Catalog", VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0), Foreground = new SolidColorBrush(Pal.Ink())
            };
            live.Checked += delegate { _live = true; };
            live.Unchecked += delegate { _live = false; };
            DockPanel.SetDock(live, Dock.Right);
            top.Children.Add(live);
            DockPanel.SetDock(top, Dock.Top);
            right.Children.Add(top);

            _status = new TextBlock { Foreground = new SolidColorBrush(Pal.Muted()), Margin = new Thickness(0, 4, 0, 12) };
            DockPanel.SetDock(_status, Dock.Top);
            right.Children.Add(_status);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _content = new StackPanel();
            scroll.Content = _content;
            right.Children.Add(scroll);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);
            Content = root;
        }

        private static UIElement Brand()
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 16, 16, 12) };
            var dot = new Border
            {
                Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Pal.Ink()),
                Child = new TextBlock
                {
                    Text = "◍",
                    Foreground = new SolidColorBrush(Pal.CardBg()), FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                }
            };
            p.Children.Add(dot);
            var t = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(new TextBlock { Text = "Driver Updater", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Pal.Ink()) });
            t.Children.Add(new TextBlock { Text = "Open · Free forever", FontSize = 11, Foreground = new SolidColorBrush(Pal.Muted()) });
            p.Children.Add(t);
            return p;
        }

        private static Button NavButton(string v)
        {
            var b = new Button
            {
                Margin = new Thickness(10, 2, 10, 2), Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Foreground = new SolidColorBrush(Pal.Ink())
            };
            if (b.Content == null) b.Content = v;
            return b;
        }

        private static Button BlueButton(string text)
        {
            return new Button
            {
                Content = text, Padding = new Thickness(18, 6, 18, 6),
                Background = new SolidColorBrush(Pal.Blue()), Foreground = Brushes.White,
                BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold
            };
        }

        private static Border Card()
        {
            return new Border
            {
                Background = new SolidColorBrush(Pal.CardBg()), CornerRadius = new CornerRadius(12),
                Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(Pal.Line()), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = Pal.Dark ? 0.4 : 0.12 }
            };
        }

        private void Show(string view)
        {
            _content.Children.Clear();
            if (view == "Overview") ShowOverview();
            else if (view == "Updates") ShowUpdates();
            else if (view == "Devices") ShowDevices();
            else if (view == "Backup") ShowBackup();
            else if (view == "Schedule") ShowSchedule();
            else if (view == "Components") ShowComponents();
            else if (view == "Win Update") ShowWinUpdate();
            else ShowExcluded();
        }

        private void ShowOverview()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(StatCard(_devices.Count.ToString(), "Devices found", 0));
            grid.Children.Add(StatCard(_updates.Count.ToString(), "Need attention", 1));
            grid.Children.Add(StatCard(Store.Backups().Count.ToString(), "Backups", 2));
            _content.Children.Add(grid);
            var how = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "How it works", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6), Foreground = new SolidColorBrush(Pal.Ink()) });
            sp.Children.Add(new TextBlock
            {
                Text = "1. Native C++ scan (SetupAPI, no PowerShell).\n2. Hardware-ID match to Microsoft Update Catalog.\n3. Automatic backup before installing. Free, no paywall.",
                Foreground = new SolidColorBrush(Pal.Muted()), TextWrapping = TextWrapping.Wrap
            });
            how.Child = sp;
            _content.Children.Add(how);
        }

        private UIElement StatCard(string k, string l, int col)
        {
            var c = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = k, FontSize = 28, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Pal.Ink()) });
            sp.Children.Add(new TextBlock { Text = l, Foreground = new SolidColorBrush(Pal.Muted()) });
            c.Child = sp;
            Grid.SetColumn(c, col);
            c.Margin = new Thickness(0, 0, 8, 12);
            return c;
        }

        private void ShowUpdates()
        {
            var bar = Card();
            var bsp = new StackPanel { Orientation = Orientation.Horizontal };
            var all = BlueButton("Update all");
            all.Click += delegate { UpdateSel(false); };
            bsp.Children.Add(all);
            var sel = new Button { Content = "Update selected", Margin = new Thickness(8, 0, 0, 0) };
            sel.Click += delegate { UpdateSel(true); };
            bsp.Children.Add(sel);
            var stop = new Button { Content = "Stop", Margin = new Thickness(8, 0, 0, 0) };
            stop.Click += delegate { Updater.Cancel(); SetStatus("Cancelling after the current driver…"); };
            bsp.Children.Add(stop);
            var rep = new Button { Content = "Export report", Margin = new Thickness(8, 0, 0, 0) };
            rep.Click += delegate { ExportReport(); };
            bsp.Children.Add(rep);
            bsp.Children.Add(new TextBlock
            {
                Text = "Backs up first. Review items are manual-only. GPU updates ask twice.",
                Foreground = new SolidColorBrush(Pal.Muted()), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            bar.Child = bsp;
            _content.Children.Add(bar);
            AddUpdateList("Ready", false);
            AddUpdateList("Needs review (manual install only)", true);
        }

        private void AddUpdateList(string header, bool review)
        {
            var h = new TextBlock { Text = header, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 6), Foreground = new SolidColorBrush(Pal.Ink()) };
            _content.Children.Add(h);
            int n = 0;
            foreach (DriverUpdate u in _updates)
            {
                bool isRev = (u.Confidence ?? "high") != "high";
                // Manual entries (no Catalog GUID) show under Ready like the
                // web UI; they carry no Update button, only Get/Exclude links.
                if (isRev != review) continue;
                if (review && u.UpdateId == "") continue;
                n++;
                var c = Card();
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 8, 0), Tag = u };
                cb.IsChecked = !review;
                sp.Children.Add(cb);
                var tx = new StackPanel();
                tx.Children.Add(new TextBlock
                {
                    Text = u.Title, FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Pal.Ink())
                });
                tx.Children.Add(new TextBlock
                {
                    Text = u.HwId + " · installed: " + (u.InstalledVersion != "" ? u.InstalledVersion : "none") +
                           " · Catalog WHQL feed, SHA-256 verified on download",
                    Foreground = new SolidColorBrush(Pal.Muted()), FontSize = 11, TextWrapping = TextWrapping.Wrap
                });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                if (u.IsGpu)
                    row.Children.Add(new TextBlock { Text = "GPU · confirm twice", Foreground = new SolidColorBrush(Pal.Muted()), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                var link = new Button { Content = "Get driver ↗", Margin = new Thickness(0, 0, 8, 0) };
                string url = u.DownloadUrl;
                link.Click += delegate { try { Process.Start(url); } catch { } };
                row.Children.Add(link);
                if (u.VendorUrl != "")
                {
                    var vl = new Button { Content = u.Vendor + " ↗", Margin = new Thickness(0, 0, 8, 0) };
                    string vurl = u.VendorUrl;
                    vl.Click += delegate { try { Process.Start(vurl); } catch { } };
                    row.Children.Add(vl);
                }
                var ex = new Button { Content = "Exclude" };
                string hw = u.HwId;
                ex.Click += delegate { Store.AddExclusion(hw); Refresh(_live); };
                row.Children.Add(ex);
                if (u.UpdateId != "")
                {
                    var one = new Button { Content = "Update", Margin = new Thickness(8, 0, 0, 0) };
                    DriverUpdate uu = u;
                    one.Click += delegate { UpdateOne(uu); };
                    row.Children.Add(one);
                }
                var sv = new Button { Content = "Skip version", Margin = new Thickness(8, 0, 0, 0) };
                string hwv = u.HwId, avv = u.AvailableVersion;
                sv.Click += delegate { Store.AddExclusion(hwv + "||" + avv); Refresh(_live); };
                row.Children.Add(sv);
                tx.Children.Add(row);
                sp.Children.Add(tx);
                c.Child = sp;
                _content.Children.Add(c);
            }
            if (n == 0)
            {
                var c = Card();
                c.Child = new TextBlock
                {
                    Text = review ? "Nothing needs a human look." : "All clear — no problem devices found.",
                    Foreground = new SolidColorBrush(Pal.Ink())
                };
                _content.Children.Add(c);
            }
        }

        private void ShowDevices()
        {
            // Virtualized list: stays fast with hundreds of devices.
            var box = new ListBox
            {
                Background = new SolidColorBrush(Pal.CardBg()),
                BorderBrush = new SolidColorBrush(Pal.Line()), BorderThickness = new Thickness(1)
            };
            foreach (Device d in _devices)
            {
                var sp = new StackPanel { Margin = new Thickness(4) };
                sp.Children.Add(new TextBlock { Text = d.DisplayName(), FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Pal.Ink()) });
                sp.Children.Add(new TextBlock
                {
                    Text = (d.PrimaryHwId() != "" ? d.PrimaryHwId() : d.InstanceId) + " · " +
                           (d.DriverVersion != "" ? d.DriverVersion : "no driver") + " · " + d.Status,
                    Foreground = new SolidColorBrush(Pal.Muted()), FontSize = 11
                });
                var item = new ListBoxItem { Content = sp };
                box.Items.Add(item);
            }
            _content.Children.Add(box);
        }

        private void ShowBackup()
        {
            var c = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Driver backup", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Pal.Ink()) });
            sp.Children.Add(new TextBlock
            {
                Text = "Exports all third-party drivers with pnputil (run elevated).",
                Foreground = new SolidColorBrush(Pal.Muted()), TextWrapping = TextWrapping.Wrap
            });
            var b = BlueButton("Back up now");
            b.Margin = new Thickness(0, 8, 0, 0);
            b.Click += delegate
            {
                SetStatus("Exporting…");
                Task.Factory.StartNew(delegate
                {
                    OpResult r = Ops.BackupAll(System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "odu-backup"));
                    Dispatcher.Invoke(new Action(delegate
                    {
                        SetStatus(r.Ok ? "Done: " + r.Path : "Failed: " + r.Log);
                        Show("Backup");
                    }));
                });
            };
            sp.Children.Add(b);
            c.Child = sp;
            _content.Children.Add(c);
            foreach (string[] bk in Store.Backups())
            {
                var e = Card();
                e.Child = new TextBlock { Text = bk[0] + " · " + bk[2], TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Pal.Ink()) };
                _content.Children.Add(e);
            }
        }

        private void ShowSchedule()
        {
            var c = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Automatic scans", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Pal.Ink()) });
            sp.Children.Add(new TextBlock
            {
                Text = "Registers a weekly Task Scheduler entry (Sunday 10:00) that runs a headless scan.",
                Foreground = new SolidColorBrush(Pal.Muted()), TextWrapping = TextWrapping.Wrap
            });
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            var on = BlueButton("Enable weekly");
            on.Click += delegate
            {
                Task.Factory.StartNew(delegate
                {
                    OpResult r = Ops.SchedEnable();
                    Dispatcher.Invoke(new Action(delegate
                    {
                        SetStatus(r.Ok ? "Weekly scan enabled." : "Failed: " + r.Log);
                        Show("Schedule");
                    }));
                });
            };
            row.Children.Add(on);
            var off = new Button { Content = "Disable", Margin = new Thickness(8, 0, 0, 0) };
            off.Click += delegate
            {
                Task.Factory.StartNew(delegate
                {
                    OpResult r = Ops.SchedDisable();
                    Dispatcher.Invoke(new Action(delegate
                    {
                        SetStatus(r.Ok ? "Disabled." : "Failed: " + r.Log);
                        Show("Schedule");
                    }));
                });
            };
            row.Children.Add(off);
            sp.Children.Add(row);
            var st = new TextBlock { Margin = new Thickness(0, 8, 0, 0), Foreground = new SolidColorBrush(Pal.Muted()) };
            sp.Children.Add(st);
            c.Child = sp;
            _content.Children.Add(c);
            Task.Factory.StartNew(delegate
            {
                OpResult s = Ops.SchedStatus();
                Dispatcher.Invoke(new Action(delegate
                {
                    st.Text = s.Ok ? "Status: enabled." : "Status: not scheduled.";
                }));
            });
        }

        private void ShowExcluded()
        {
            List<string> excl = Store.Exclusions();
            if (excl.Count == 0)
            {
                var c = Card();
                c.Child = new TextBlock { Text = "Nothing excluded.", Foreground = new SolidColorBrush(Pal.Ink()) };
                _content.Children.Add(c);
                return;
            }
            foreach (string h in excl)
            {
                string hw = h;
                var c = Card();
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock
                {
                    Text = hw, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Pal.Muted()), Width = 640,
                    TextWrapping = TextWrapping.Wrap
                });
                var rm = new Button { Content = "Remove", Margin = new Thickness(8, 0, 0, 0) };
                rm.Click += delegate { Store.RemoveExclusion(hw); ShowExcluded(); };
                sp.Children.Add(rm);
                c.Child = sp;
                _content.Children.Add(c);
            }
        }

        private void SetStatus(string s) { _status.Text = s; }

        private void ShowComponents()
        {
            var c = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Game & runtime components", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Pal.Ink()) });
            sp.Children.Add(new TextBlock
            {
                Text = "VC++ redistributables and DirectX from official Microsoft links. Checks the local registry only.",
                Foreground = new SolidColorBrush(Pal.Muted()), TextWrapping = TextWrapping.Wrap
            });
            c.Child = sp;
            _content.Children.Add(c);
            Task.Factory.StartNew(delegate
            {
                var items = new System.Collections.Generic.List<string[]>();
                try
                {
                    using (var ps = new System.Diagnostics.Process())
                    {
                        ps.StartInfo.FileName = "powershell.exe";
                        ps.StartInfo.Arguments = "-NoProfile -Command \"Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\*' -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -like 'Microsoft Visual C++*Redistributable*' } | Select-Object -ExpandProperty DisplayName\"";
                        ps.StartInfo.UseShellExecute = false;
                        ps.StartInfo.RedirectStandardOutput = true;
                        ps.StartInfo.CreateNoWindow = true;
                        ps.Start();
                        string vc = ps.StandardOutput.ReadToEnd();
                        ps.WaitForExit(60000);
                        items.Add(new string[] { "Visual C++ Redistributable",
                            (vc.Trim() != "" ? "installed" : "missing") + " — " + "https://aka.ms/vs/17/release/vc_redist.x64.exe" });
                    }
                }
                catch { }
                bool dx = System.IO.File.Exists(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "D3DCompiler_47.dll"));
                items.Add(new string[] { "DirectX runtime",
                    (dx ? "present" : "missing") + " — https://www.microsoft.com/en-us/download/details.aspx?id=35" });
                Dispatcher.Invoke(new Action(delegate
                {
                    foreach (string[] it in items)
                    {
                        var e = Card();
                        e.Child = new TextBlock { Text = it[0] + ": " + it[1], TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Pal.Ink()) };
                        _content.Children.Add(e);
                    }
                }));
            });
        }

        private void ShowWinUpdate()
        {
            var c = Card();
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = "Windows Update drivers", FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Pal.Ink()) });
            sp.Children.Add(new TextBlock
            {
                Text = "Driver updates offered by Windows Update itself (same engine as the Python service).",
                Foreground = new SolidColorBrush(Pal.Muted()), TextWrapping = TextWrapping.Wrap
            });
            var b = BlueButton("Check now");
            b.Margin = new Thickness(0, 8, 0, 0);
            b.Click += delegate
            {
                SetStatus("Asking Windows Update (can take minutes)…");
                Task.Factory.StartNew(delegate
                {
                    string log = WinUpdateSearch();
                    Dispatcher.Invoke(new Action(delegate { SetStatus(log); }));
                });
            };
            sp.Children.Add(b);
            c.Child = sp;
            _content.Children.Add(c);
        }

        private string WinUpdateSearch()
        {
            try
            {
                using (var ps = new System.Diagnostics.Process())
                {
                    ps.StartInfo.FileName = "powershell.exe";
                    ps.StartInfo.Arguments = "-NoProfile -Command \"$s = New-Object -ComObject Microsoft.Update.Session; $r = $s.CreateUpdateSearcher().Search(\"IsInstalled=0 and IsHidden=0 and Type='Driver'\"); 'COUNT:' + $r.Updates.Count; foreach ($u in $r.Updates) { $u.Title }\"";
                    ps.StartInfo.UseShellExecute = false;
                    ps.StartInfo.RedirectStandardOutput = true;
                    ps.StartInfo.CreateNoWindow = true;
                    ps.Start();
                    string output = ps.StandardOutput.ReadToEnd();
                    ps.WaitForExit(5 * 60 * 1000);
                    string[] lines = output.Split(new char[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
                    int n = 0;
                    foreach (string ln in lines) if (ln.StartsWith("COUNT:")) n = 1;
                    return n > 0 ? output.Trim() : "No driver updates from Windows Update.";
                }
            }
            catch (Exception e) { return "Check failed: " + e.Message; }
        }

        private void Refresh(bool live)
        {
            SetStatus(live ? "Live scan — contacting Microsoft Update Catalog…" : "Scanning…");
            Task.Factory.StartNew(delegate
            {
                List<Device> devs = NativeScan.Scan(120000);
                List<string> excl = Store.Exclusions();
                List<DriverUpdate> ups = Catalog.Resolve(devs, excl, live);
                string reboot = "";
                try { if (Ops.RebootPending(out reboot) && reboot == "") reboot = "pending"; }
                catch { }
                Dispatcher.Invoke(new Action(delegate
                {
                    _devices = devs;
                    _updates = ups;
                    _pill.Text = ups.Count.ToString();
                    SetStatus(devs.Count + " devices, " + ups.Count + " need attention (" +
                              ArchUtil.NativeArch() + ")." +
                              (reboot != "" ? " Restart required: " + reboot + "." : ""));
                    Show("Overview");
                    CheckAppUpdate();
                }));
            });
        }

        private void CheckAppUpdate()
        {
            Task.Factory.StartNew(delegate
            {
                string latest = "";
                try
                {
                    using (var wc = new System.Net.WebClient())
                    {
                        wc.Headers.Add("User-Agent", "open-driver-updater");
                        string js = wc.DownloadString(
                            "https://api.github.com/repos/alphingj/open-driver-updater/releases/latest");
                        var tag = System.Text.RegularExpressions.Regex.Match(js, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
                        if (tag.Success) latest = tag.Groups[1].Value;
                    }
                }
                catch { return; }
                if (latest != "" && latest != AppVersion.Value)
                {
                    Dispatcher.Invoke(new Action(delegate
                    {
                        SetStatus("Update available: v" + AppVersion.Value + " → v" + latest +
                                  ". Download it from the GitHub releases page.");
                    }));
                }
            });
        }

        private void UpdateSel(bool selectedOnly)
        {
            var live = new List<DriverUpdate>();
            if (selectedOnly)
            {
                if (FindChecks(_content).Count == 0)
                {
                    MessageBox.Show("Open the Updates view to select drivers first.",
                                    "Open Driver Updater");
                    return;
                }
                foreach (DriverUpdate u in _updates)
                {
                    if (u.UpdateId == "" || (u.Confidence ?? "high") != "high") continue;
                    if (IsChecked(u)) live.Add(u);
                }
                if (live.Count == 0)
                {
                    MessageBox.Show("Nothing selected (review items are manual-only).", "Open Driver Updater");
                    return;
                }
            }
            else
            {
                foreach (DriverUpdate u in _updates)
                    if (u.UpdateId != "" && (u.Confidence ?? "high") == "high") live.Add(u);
                if (live.Count == 0)
                {
                    MessageBox.Show("Run a Live Catalog scan first.", "Open Driver Updater");
                    return;
                }
            }
            RunList(live);
        }

        // One click: download + install exactly this driver (restore point +
        // backup run first, same as bulk). Review matches allowed with notice.
        private void UpdateOne(DriverUpdate u)
        {
            var one = new List<DriverUpdate>();
            one.Add(u);
            string msg = "Download and install now?\n" + u.DeviceName + "\n→ " + u.AvailableVersion
                + (u.IsGpu ? "\n\nWARNING: display/GPU driver — can black-screen mid-install." : "")
                + (((u.Confidence ?? "high") != "high") ? "\n\nNote: needs-review match, not auto-install grade." : "")
                + "\nA restore point + backup run first. Admin rights required.";
            if (MessageBox.Show(msg, "Open Driver Updater", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            if (u.IsGpu && MessageBox.Show("Confirm the GPU update a second time to proceed.",
                    "Open Driver Updater", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            RunList(one);
        }

        private void RunList(List<DriverUpdate> live)
        {
            int gpus = 0;
            foreach (DriverUpdate u in live) if (u.IsGpu) gpus++;
            string msg = "Install " + live.Count + " driver updates? A restore point + backup run first. Admin rights required.";
            if (gpus > 0)
                msg += "\n\nWARNING: " + gpus + " display/GPU driver(s) included — these can black-screen mid-install. Only continue if you can reboot.";
            if (MessageBox.Show(msg, "Open Driver Updater", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            if (gpus > 0 && MessageBox.Show("Confirm GPU updates a second time to proceed.",
                    "Open Driver Updater", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;
            string arch = ArchUtil.NativeArch();
            Task.Factory.StartNew(delegate
            {
                List<JobItem> res = Updater.RunAll(live, arch, delegate(string s)
                {
                    try { Dispatcher.Invoke(new Action(delegate { SetStatus(s); })); }
                    catch { }
                });
                try
                {
                    Dispatcher.Invoke(new Action(delegate
                    {
                        ShowResults(res);
                        Refresh(_live);
                    }));
                }
                catch { }
            });
        }

        // Checkbox state is read live from the Updates view when open.
        private bool IsChecked(DriverUpdate u)
        {
            foreach (object el in _content.Children)
            {
                Border c = el as Border;
                if (c == null) continue;
                foreach (CheckBox cb in FindChecks(c))
                    if (cb.Tag == (object)u) return cb.IsChecked == true;
            }
            return true;
        }

        private static System.Collections.Generic.List<CheckBox> FindChecks(DependencyObject root)
        {
            var out_ = new System.Collections.Generic.List<CheckBox>();
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                DependencyObject ch = VisualTreeHelper.GetChild(root, i);
                CheckBox cb = ch as CheckBox;
                if (cb != null) out_.Add(cb);
                else out_.AddRange(FindChecks(ch));
            }
            return out_;
        }

        private void ShowResults(List<JobItem> res)
        {
            var win = new Window
            {
                Title = "Update results", Width = 700, Height = 480,
                Background = new SolidColorBrush(Pal.Page())
            };
            var sp = new StackPanel { Margin = new Thickness(12) };
            int ok = 0;
            foreach (JobItem j in res) if (j.Ok) ok++;
            sp.Children.Add(new TextBlock
            {
                Text = "Done " + ok + "/" + res.Count + ". A restart may be required.",
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8),
                Foreground = new SolidColorBrush(Pal.Ink())
            });
            foreach (JobItem j in res)
            {
                JobItem jj = j;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(new TextBlock
                {
                    Text = (jj.Ok ? "✓ " : "✗ ") + jj.Device + " — " + jj.Detail,
                    Width = 520, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Pal.Ink())
                });
                if (jj.Ok && jj.Published != "")
                {
                    var rb = new Button { Content = "Rollback", Margin = new Thickness(8, 0, 0, 0) };
                    rb.Click += delegate
                    {
                        OpResult r = Ops.RollbackDriver(jj.Published);
                        MessageBox.Show(r.Ok ? "Rolled back " + jj.Published + "." : "Rollback failed: " + r.Log,
                                        "Open Driver Updater");
                    };
                    row.Children.Add(rb);
                }
                sp.Children.Add(row);
            }
            win.Content = new ScrollViewer { Content = sp };
            win.ShowDialog();
            SetStatus("Done " + ok + "/" + res.Count + ". A restart may be required.");
        }

        private void ExportReport()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.FileName = "driver-report.html";
            dlg.Filter = "HTML report|*.html";
            if (dlg.ShowDialog() != true) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>report</title></head><body>");
                sb.Append("<h1>Open Driver Updater report</h1><h2>Updates</h2><ul>");
                foreach (DriverUpdate u in _updates)
                    sb.Append("<li>").Append(System.Security.SecurityElement.Escape(u.DeviceName))
                      .Append(" — ").Append(System.Security.SecurityElement.Escape(u.AvailableVersion)).Append("</li>");
                sb.Append("</ul><h2>Devices</h2><ul>");
                foreach (Device d in _devices)
                    sb.Append("<li>").Append(System.Security.SecurityElement.Escape(d.DisplayName()))
                      .Append(" — ").Append(System.Security.SecurityElement.Escape(d.DriverVersion)).Append("</li>");
                sb.Append("</ul></body></html>");
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
                SetStatus("Report saved.");
            }
            catch (Exception e) { SetStatus("Report failed: " + e.Message); }
        }
    }
}
