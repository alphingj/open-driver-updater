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
            string[] views = new string[] { "Overview", "Updates", "Devices", "Backup", "Schedule", "Excluded" };
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
            all.Click += delegate { UpdateAll(); };
            bsp.Children.Add(all);
            var stop = new Button { Content = "Stop", Margin = new Thickness(8, 0, 0, 0) };
            stop.Click += delegate { Updater.Cancel(); SetStatus("Cancelling after the current driver…"); };
            bsp.Children.Add(stop);
            bsp.Children.Add(new TextBlock
            {
                Text = "Backs up first, then downloads + installs every live update.",
                Foreground = new SolidColorBrush(Pal.Muted()), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            bar.Child = bsp;
            _content.Children.Add(bar);
            if (_updates.Count == 0)
            {
                var c = Card();
                c.Child = new TextBlock { Text = "All clear — no problem devices found.", Foreground = new SolidColorBrush(Pal.Ink()) };
                _content.Children.Add(c);
                return;
            }
            foreach (DriverUpdate u in _updates)
            {
                var c = Card();
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = u.UpdateId != "" ? u.Title : u.DeviceName,
                    FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Pal.Ink())
                });
                sp.Children.Add(new TextBlock
                {
                    Text = u.HwId + " · installed: " + (u.InstalledVersion != "" ? u.InstalledVersion : "none"),
                    Foreground = new SolidColorBrush(Pal.Muted()), FontSize = 11, TextWrapping = TextWrapping.Wrap
                });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
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
                sp.Children.Add(row);
                c.Child = sp;
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

        private void Refresh(bool live)
        {
            SetStatus(live ? "Live scan — contacting Microsoft Update Catalog…" : "Scanning…");
            Task.Factory.StartNew(delegate
            {
                List<Device> devs = NativeScan.Scan(120000);
                List<string> excl = Store.Exclusions();
                List<DriverUpdate> ups = Catalog.Resolve(devs, excl, live);
                Dispatcher.Invoke(new Action(delegate
                {
                    _devices = devs;
                    _updates = ups;
                    _pill.Text = ups.Count.ToString();
                    SetStatus(devs.Count + " devices, " + ups.Count + " need attention (" +
                              ArchUtil.NativeArch() + ").");
                    Show("Overview");
                }));
            });
        }

        private void UpdateAll()
        {
            var live = new List<DriverUpdate>();
            foreach (DriverUpdate u in _updates)
                if (u.UpdateId != "") live.Add(u);
            if (live.Count == 0)
            {
                MessageBox.Show("Run a Live Catalog scan first.", "Open Driver Updater");
                return;
            }
            if (MessageBox.Show("Install " + live.Count + " driver updates? A backup runs first. Admin rights required.",
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
                        int ok = 0;
                        foreach (JobItem j in res) if (j.Ok) ok++;
                        SetStatus("Done " + ok + "/" + res.Count + ". A restart may be required.");
                        MessageBox.Show("Installed " + ok + " of " + res.Count + ". A restart may be required.",
                                        "Open Driver Updater");
                        Refresh(_live);
                    }));
                }
                catch { }
            });
        }
    }
}
