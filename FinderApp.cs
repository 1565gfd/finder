// FinderApp - оконное приложение поиска файлов (WPF, без консоли).
// Тёмно-синяя тема, моноширинный шрифт, анимации. Файлы НЕ открываются.
// Двойной клик по результату — открыть его папку в проводнике.
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using IOPath = System.IO.Path;

// =====================================================================
//  FINDER — авторский проект.  Водяной знак владельца встроен в код и
//  интерфейс в зашифрованном виде (см. _sig / Sig()).  Не удалять.
// =====================================================================
class FinderApp
{
    // Подпись владельца, XOR-обфускация. Используется в заголовке окна,
    // в шапке интерфейса, в подсказке и на заставке. Удаление ломает сборку.
    static readonly byte[] _sig = { 0x96, 0x81, 0xF7, 0xFB, 0xBC, 0x8E, 0x91 };
    static string Sig()
    {
        var b = new byte[_sig.Length];
        for (int i = 0; i < _sig.Length; i++) b[i] = (byte)(_sig[i] ^ ((0xA7 + i * 13) & 0xFF));
        return Encoding.UTF8.GetString(b);
    }

    // ---- палитра (сине-чёрная) ----
    static SolidColorBrush BgTop  = B("#0A0E1A");
    static SolidColorBrush BgBot  = B("#05060B");
    static SolidColorBrush Panel  = B("#0E1220");
    static SolidColorBrush Card   = B("#141A2B");
    static SolidColorBrush CardHi = B("#1D2740");
    static SolidColorBrush Line   = B("#22305A");
    static SolidColorBrush Text   = B("#E6EAF5");
    static SolidColorBrush Sub    = B("#6B7690");
    static SolidColorBrush Blue   = B("#3B6BFF");
    static SolidColorBrush BlueHi = B("#5B8CFF");
    static SolidColorBrush Green  = B("#34D399");
    static SolidColorBrush Red    = B("#F87171");
    static FontFamily Mono = new FontFamily("Consolas, Cascadia Mono, monospace");

    static TextBox inWhat, inWhere;
    static TextBlock status, counter, phWhat, phWhere;
    static ListBox results;
    static Border findBtn, spinner;
    static TextBlock findLbl;
    static SolidColorBrush findBg;
    static RotateTransform spinRot;
    static string mode = "contains";
    static Window mainWin;
    static Dictionary<string, Border> chips = new Dictionary<string, Border>();
    static volatile bool searching = false;
    static volatile bool canceled = false;
    static volatile bool searchDone = false;

    // многопоточный движок
    static ConcurrentQueue<string> dirQ;
    static ConcurrentQueue<string> resultQ;
    static int pending;          // сколько папок ещё в обработке
    static int activeThreads;    // сколько потоков ещё живо
    static int foundTotal;       // атомарный счётчик найденного
    static DispatcherTimer drainTimer;
    static System.Diagnostics.Stopwatch sw;   // таймер поиска

    // ---- быстрый Win32 обход ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WIN32_FIND_DATA
    {
        public FileAttributes dwFileAttributes;
        public uint ftCreationLow, ftCreationHigh;
        public uint ftAccessLow, ftAccessHigh;
        public uint ftWriteLow, ftWriteHigh;
        public uint nFileSizeHigh, nFileSizeLow;
        public uint dwReserved0, dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternate;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr FindFirstFileEx(string lpFileName, int infoLevel, out WIN32_FIND_DATA data,
        int searchOp, IntPtr filter, int flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool FindNextFile(IntPtr h, out WIN32_FIND_DATA data);
    [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);
    static readonly IntPtr INVALID = new IntPtr(-1);

    [STAThread]
    static void Main()
    {
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnLastWindowClose;
        var splash = BuildSplash();
        splash.Show();
        app.Run();
    }

    // ================= заставка «установки/загрузки» =================
    static Window BuildSplash()
    {
        var sp = new Window
        {
            Width = 460, Height = 300,
            WindowStyle = WindowStyle.None, AllowsTransparency = true,
            Background = Brushes.Transparent, Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Title = "Установка FINDER"
        };
        ImageSource icoSrc = null, pngSrc = null;
        try
        {
            string ico = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "finder.ico");
            if (File.Exists(ico)) { icoSrc = BitmapFrame.Create(new Uri(ico)); sp.Icon = icoSrc; }
            string png = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "finder.png");
            if (File.Exists(png))
            {
                var b = new BitmapImage();
                b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(png); b.EndInit();
                pngSrc = b;
            }
        }
        catch { }

        var root = new Border
        {
            CornerRadius = new CornerRadius(18), Margin = new Thickness(10),
            BorderBrush = Line, BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(BgTop.Color, BgBot.Color, new Point(0, 0), new Point(0.6, 1)),
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black }
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 40, 0) };

        var imgSrc = pngSrc ?? icoSrc;
        if (imgSrc != null)
        {
            var im = new Image { Source = imgSrc, Width = 88, Height = 88, HorizontalAlignment = HorizontalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
            stack.Children.Add(im);
        }
        stack.Children.Add(new TextBlock { Text = "FINDER", Foreground = Text, FontFamily = Mono, FontSize = 26,
            FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 2) });
        stack.Children.Add(new TextBlock { Text = "установка", Foreground = Sub, FontFamily = Mono, FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) });

        const double TRACK = 360;
        var track = new Border { Height = 8, Width = TRACK, Background = Card, CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Center, BorderBrush = Line, BorderThickness = new Thickness(1) };
        var fill = new Border { Height = 8, Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4),
            Background = new LinearGradientBrush(Blue.Color, BlueHi.Color, new Point(0, 0), new Point(1, 0)),
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8, Color = BlueHi.Color } };
        track.Child = fill;
        stack.Children.Add(track);

        var row = new Grid { Width = TRACK, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stat = new TextBlock { Text = "инициализация…", Foreground = Sub, FontFamily = Mono, FontSize = 11 };
        var pct = new TextBlock { Text = "0%", Foreground = BlueHi, FontFamily = Mono, FontSize = 11, FontWeight = FontWeights.Bold };
        Grid.SetColumn(pct, 1);
        row.Children.Add(stat); row.Children.Add(pct);
        stack.Children.Add(row);

        stack.Children.Add(new TextBlock { Text = "© " + Sig(), Foreground = Sub, FontFamily = Mono, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0), Opacity = 0.7 });

        root.Child = stack; sp.Content = root;

        sp.Loaded += (s, e) =>
        {
            var anim = new DoubleAnimation(0, TRACK, TimeSpan.FromMilliseconds(2600)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (a, b) =>
            {
                int p = (int)Math.Round(fill.ActualWidth / TRACK * 100);
                if (p > 100) p = 100;
                pct.Text = p + "%";
                stat.Text = p < 18 ? "инициализация…" : p < 42 ? "загрузка модулей поиска…" :
                            p < 66 ? "подключение дисков…" : p < 88 ? "подготовка интерфейса…" : "почти готово…";
            };
            anim.Completed += (a, b) =>
            {
                timer.Stop(); pct.Text = "100%"; stat.Text = "готово"; stat.Foreground = Green;
                var main = Build();
                main.Show();
                var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(320));
                fo.Completed += (c, d) => sp.Close();
                sp.BeginAnimation(UIElement.OpacityProperty, fo);
            };
            timer.Start();
            fill.BeginAnimation(FrameworkElement.WidthProperty, anim);
        };

        sp.Opacity = 0;
        sp.ContentRendered += (s, e) => sp.BeginAnimation(UIElement.OpacityProperty, DA(0, 1, 300));
        return sp;
    }

    static Window Build()
    {
        var win = new Window
        {
            Width = 860, Height = 600,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            MinWidth = 640, MinHeight = 420,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Title = "FINDER · " + Sig()   // водяной знак в заголовке
        };
        try
        {
            string ico = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "finder.ico");
            if (File.Exists(ico)) win.Icon = BitmapFrame.Create(new Uri(ico));
        }
        catch { }

        var root = new Border
        {
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(12),
            BorderBrush = Line, BorderThickness = new Thickness(1),
            Background = new LinearGradientBrush(BgTop.Color, BgBot.Color, new Point(0, 0), new Point(0.5, 1)),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black }
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // титул
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // ввод
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // чипы
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // статус

        // ===== титул =====
        var tbar = new Grid { Margin = new Thickness(18, 14, 12, 6) };
        tbar.ColumnDefinitions.Add(new ColumnDefinition());
        tbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var brandImg = LoadIconPng();
        if (brandImg != null)
        {
            var bi = new Image { Source = brandImg, Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(bi, BitmapScalingMode.HighQuality);
            brand.Children.Add(bi);
        }
        else brand.Children.Add(IconGlyph(26));
        var bt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(new TextBlock { Text = "FINDER", Foreground = Text, FontFamily = Mono, FontSize = 18, FontWeight = FontWeights.Bold });
        bt.Children.Add(new TextBlock { Text = "поиск файлов · © " + Sig(), Foreground = Sub, FontFamily = Mono, FontSize = 11 });
        brand.ToolTip = "FINDER · автор " + Sig();
        brand.Children.Add(bt);
        tbar.Children.Add(brand);

        var wbtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var minB = WinBtn("—", CardHi.Color);
        var clsB = WinBtn("✕", ((SolidColorBrush)Red).Color);
        // ловим НАЖАТИЕ и гасим событие, чтобы перетаскивание заголовка не перехватило клик
        minB.MouseLeftButtonDown += (s, e) => { e.Handled = true; win.WindowState = WindowState.Minimized; };
        clsB.MouseLeftButtonDown += (s, e) => { e.Handled = true; win.Close(); };
        wbtns.Children.Add(minB); wbtns.Children.Add(clsB);
        Grid.SetColumn(wbtns, 1); tbar.Children.Add(wbtns);
        tbar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { win.DragMove(); } catch { } };
        Grid.SetRow(tbar, 0); grid.Children.Add(tbar);

        // ===== строка ввода =====
        var ir = new Grid { Margin = new Thickness(18, 8, 18, 6) };
        ir.ColumnDefinitions.Add(new ColumnDefinition());
        ir.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        ir.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Border bW; inWhat = Input("что искать: имя или *.pdf", out bW, out phWhat);
        Border bWh; inWhere = Input("где: диск или папка (E:\\)", out bWh, out phWhere);
        bWh.Margin = new Thickness(10, 0, 10, 0);
        findBtn = FindButton();

        Grid.SetColumn(bW, 0); Grid.SetColumn(bWh, 1); Grid.SetColumn(findBtn, 2);
        ir.Children.Add(bW); ir.Children.Add(bWh); ir.Children.Add(findBtn);
        Grid.SetRow(ir, 1); grid.Children.Add(ir);

        inWhat.KeyDown += (s, e) => { if (e.Key == Key.Enter) Start(); };
        inWhere.KeyDown += (s, e) => { if (e.Key == Key.Enter) Start(); };

        // ===== чипы режима =====
        var cr = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 2, 18, 8) };
        cr.Children.Add(Chip("contains", "по имени"));
        cr.Children.Add(Chip("wild", "маска *"));
        cr.Children.Add(Chip("exact", "точно"));
        cr.Children.Add(Chip("ext", "расширение"));
        SelectChip("contains");
        cr.Children.Add(AllPcButton());
        Grid.SetRow(cr, 2); grid.Children.Add(cr);

        // ===== результаты =====
        var lc = new Border { Background = Panel, CornerRadius = new CornerRadius(12), Margin = new Thickness(18, 4, 18, 6),
            BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(6) };
        results = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Text, FontFamily = Mono, FontSize = 12.5 };
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Auto);
        results.ItemContainerStyle = ItemStyle();
        results.MouseDoubleClick += OpenFolder;
        lc.Child = results;
        Grid.SetRow(lc, 3); grid.Children.Add(lc);

        // ===== статус =====
        var sb = new Grid { Margin = new Thickness(22, 2, 22, 14) };
        sb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sb.ColumnDefinitions.Add(new ColumnDefinition());
        sb.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        spinner = Spinner(); spinner.Visibility = Visibility.Collapsed;
        Grid.SetColumn(spinner, 0); sb.Children.Add(spinner);
        status = new TextBlock { Text = "введите запрос и нажмите «Найти» (или Enter)", Foreground = Sub, FontFamily = Mono,
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(status, 1); sb.Children.Add(status);
        counter = new TextBlock { Text = "", Foreground = Green, FontFamily = Mono, FontSize = 13,
            FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(counter, 2); sb.Children.Add(counter);
        Grid.SetRow(sb, 4); grid.Children.Add(sb);

        root.Child = grid;
        win.Content = root;

        win.KeyDown += (s, e) => { if (e.Key == Key.Escape) win.Close(); };
        mainWin = win;

        var slide = new TranslateTransform(0, 24);
        root.RenderTransform = slide;
        win.Opacity = 0;
        win.Loaded += (s, e) =>
        {
            win.BeginAnimation(UIElement.OpacityProperty, DA(0, 1, 320));
            var a = DA(24, 0, 420); a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            slide.BeginAnimation(TranslateTransform.YProperty, a);
            inWhat.Focus();
        };
        return win;
    }

    // ================= поиск =================
    static void Start() { StartCore(false); }       // обычный поиск — требует диск/папку
    static void StartAll() { StartCore(true); }      // поиск по всему компьютеру

    static void StartCore(bool allPc)
    {
        if (searching) return;
        string what = inWhat.Text.Trim();
        if (what.Length == 0) { status.Foreground = Red; status.Text = "введите, что искать"; Pulse(status); return; }

        var roots = new List<string>();
        if (allPc)
        {
            foreach (var d in DriveInfo.GetDrives()) if (d.IsReady) roots.Add(d.RootDirectory.FullName);
        }
        else
        {
            string where = inWhere.Text.Trim();
            if (where.Length == 0)
            {
                MessageBox.Show(mainWin,
                    "Укажите диск или папку в поле «где» (например  E:\\ ).\n\nЛибо нажмите кнопку «Весь ПК» — тогда поиск пойдёт по всем дискам.",
                    "Не задана папка", MessageBoxButton.OK, MessageBoxImage.Warning);
                status.Foreground = Red; status.Text = "укажите диск или папку"; Pulse(status);
                return;
            }
            if (!Directory.Exists(where))
            {
                MessageBox.Show(mainWin,
                    "Папка не найдена:\n" + where + "\n\nУкажите существующую папку (например  E:\\ ) или нажмите «Весь ПК».",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                status.Foreground = Red; status.Text = "папка не найдена";
                return;
            }
            roots.Add(where);
        }

        results.Items.Clear();
        counter.Text = "";
        canceled = false;
        searching = true;
        findLbl.Text = "СТОП"; AC(findBg, findBg.Color, Red.Color);
        spinner.Visibility = Visibility.Visible; StartSpin();
        status.Foreground = Sub; status.Text = allPc ? "поиск по всему компьютеру…  нажмите СТОП" : "идёт поиск…  нажмите СТОП чтобы прервать";

        string q = what.ToLowerInvariant();
        string curMode = mode;
        string pat; bool wild;
        if (curMode == "ext") { pat = ("*." + q.TrimStart('.')); wild = true; }
        else if (curMode == "wild") { pat = q; wild = true; }
        else { pat = q; wild = false; }

        // ---- запуск параллельного поиска ----
        dirQ = new ConcurrentQueue<string>();
        resultQ = new ConcurrentQueue<string>();
        foundTotal = 0; searchDone = false;
        pending = roots.Count;
        foreach (var r in roots) dirQ.Enqueue(r);

        sw = System.Diagnostics.Stopwatch.StartNew();
        int n = Environment.ProcessorCount; if (n < 4) n = 4; if (n > 12) n = 12;
        activeThreads = n;
        for (int i = 0; i < n; i++)
        {
            var t = new Thread(() => Worker(pat, wild, curMode)) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            t.Start();
        }

        drainTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        drainTimer.Tick += (s, e) => Drain();
        drainTimer.Start();
    }

    // рабочий поток: тянет папки из очереди, перечисляет через Win32
    static void Worker(string pat, bool wild, string curMode)
    {
        var subs = new List<string>();
        while (true)
        {
            if (canceled) break;
            string dir;
            if (!dirQ.TryDequeue(out dir))
            {
                if (Volatile.Read(ref pending) == 0) break;
                Thread.Sleep(1);
                continue;
            }
            subs.Clear();
            EnumDir(dir, pat, wild, curMode, subs);
            if (subs.Count > 0)
            {
                Interlocked.Add(ref pending, subs.Count);
                foreach (var s in subs) dirQ.Enqueue(s);
            }
            Interlocked.Decrement(ref pending);
        }
        if (Interlocked.Decrement(ref activeThreads) == 0) searchDone = true;
    }

    static void EnumDir(string dir, string pat, bool wild, string curMode, List<string> subs)
    {
        string bslash = dir.EndsWith("\\") ? dir : dir + "\\";
        WIN32_FIND_DATA fd;
        // infoLevel=1 (Basic, без короткого имени), searchOp=0, flags=2 (LARGE_FETCH)
        IntPtr h = FindFirstFileEx(bslash + "*", 1, out fd, 0, IntPtr.Zero, 2);
        if (h == INVALID) return;
        try
        {
            do
            {
                string name = fd.cFileName;
                if (name == "." || name == "..") continue;
                bool isDir = (fd.dwFileAttributes & FileAttributes.Directory) != 0;
                if (isDir)
                {
                    if ((fd.dwFileAttributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // пропускаем только junction (0xA0000003) и symlink (0xA000000C) —
                        // они могут зацикливать обход. Облачные папки OneDrive и прочие теги обходим.
                        uint tag = fd.dwReserved0;
                        if (tag == 0xA0000003 || tag == 0xA000000C) continue;
                    }
                    subs.Add(bslash + name);
                }
                else
                {
                    string low = name.ToLowerInvariant();
                    bool m = curMode == "exact" ? low == pat
                           : wild ? Wild(low, pat)
                           : low.IndexOf(pat, StringComparison.Ordinal) >= 0;
                    if (m)
                    {
                        resultQ.Enqueue(bslash + name);
                        Interlocked.Increment(ref foundTotal);
                    }
                }
            } while (FindNextFile(h, out fd));
        }
        finally { FindClose(h); }
    }

    // UI-поток: сливает найденное в список порциями
    static void Drain()
    {
        int added = 0;
        string p;
        while (added < 4000 && resultQ.TryDequeue(out p))
        {
            if (results.Items.Count < 50000) results.Items.Add(p);
            added++;
        }
        counter.Text = Fmt(sw.Elapsed) + " · " + foundTotal + " найдено";
        if (searchDone && resultQ.IsEmpty)
        {
            drainTimer.Stop();
            Done();
        }
    }

    static void Done()
    {
        searching = false; StopSpin();
        if (sw != null) sw.Stop();
        spinner.Visibility = Visibility.Collapsed;
        findLbl.Text = "НАЙТИ"; AC(findBg, findBg.Color, Blue.Color);
        counter.Text = Fmt(sw.Elapsed) + " · " + foundTotal + " найдено";
        if (canceled)
        {
            status.Foreground = Sub;
            status.Text = "остановлено · найдено " + foundTotal;
        }
        else
        {
            status.Foreground = foundTotal > 0 ? Green : Sub;
            status.Text = foundTotal > 0 ? "готово · двойной клик по файлу — открыть его папку" : "ничего не найдено";
        }
        if (results.Items.Count >= 50000)
            status.Text = "показаны первые 50000 · всего найдено " + foundTotal;
        Pulse(counter);

        // стандартное окно Windows, если ничего не найдено
        if (!canceled && foundTotal == 0)
        {
            string q = inWhat.Text.Trim();
            mainWin.Dispatcher.BeginInvoke((Action)(() =>
                MessageBox.Show(mainWin,
                    "По запросу «" + q + "» ничего не найдено.\n\nПроверьте написание, смените режим (по имени / маска / расширение) или расширьте область поиска.",
                    "Поиск файлов", MessageBoxButton.OK, MessageBoxImage.Information)));
        }
    }

    static void OpenFolder(object s, MouseButtonEventArgs e)
    {
        var sel = results.SelectedItem as string;
        if (sel == null) return;
        // защита: открываем только реально существующий путь и только проводник
        if (!File.Exists(sel)) { status.Foreground = Red; status.Text = "файл больше не существует"; return; }
        try
        {
            // абсолютный путь к системному explorer.exe — чтобы нельзя было подсунуть свой
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string explorer = IOPath.Combine(winDir, "explorer.exe");
            if (!File.Exists(explorer)) explorer = "explorer.exe";
            var psi = new ProcessStartInfo
            {
                FileName = explorer,
                Arguments = "/select,\"" + sel + "\"",
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch { status.Foreground = Red; status.Text = "не удалось открыть папку"; }
    }

    // ================= элементы =================
    static TextBox Input(string ph, out Border host, out TextBlock place)
    {
        var tb = new TextBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Text, CaretBrush = BlueHi, FontFamily = Mono, FontSize = 13.5,
            VerticalContentAlignment = VerticalAlignment.Center };
        var pt = new TextBlock { Text = ph, Foreground = Sub, FontFamily = Mono, FontSize = 13,
            IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        var g = new Grid(); g.Children.Add(pt); g.Children.Add(tb);
        tb.TextChanged += (s, e) => pt.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var h = new Border { Background = Card, CornerRadius = new CornerRadius(10),
            BorderBrush = Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 10), Child = g };
        tb.GotFocus += (s, e) => h.BorderBrush = Blue;
        tb.LostFocus += (s, e) => h.BorderBrush = Line;
        host = h; place = pt; return tb;
    }

    static Border FindButton()
    {
        findBg = new SolidColorBrush(Blue.Color);
        findLbl = new TextBlock { Text = "НАЙТИ", Foreground = Brushes.White, FontFamily = Mono,
            FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Background = findBg, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
            Padding = new Thickness(28, 0, 28, 0), MinWidth = 120, Child = findLbl };
        b.MouseEnter += (s, e) => b.Opacity = 0.9;
        b.MouseLeave += (s, e) => b.Opacity = 1.0;
        b.MouseLeftButtonUp += (s, e) => Toggle();
        return b;
    }

    // кнопка работает как НАЙТИ / СТОП
    static void Toggle()
    {
        if (searching) { canceled = true; status.Foreground = Sub; status.Text = "останавливаю…"; }
        else Start();
    }

    static Border Chip(string key, string label)
    {
        var bg = new SolidColorBrush(Card.Color);
        var b = new Border { Background = bg, CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand,
            BorderBrush = Line, BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock { Text = label, Foreground = Sub, FontFamily = Mono, FontSize = 12 } };
        b.MouseLeftButtonUp += (s, e) => SelectChip(key);
        chips[key] = b;
        return b;
    }

    // отдельная кнопка: поиск по всем дискам компьютера
    static Border AllPcButton()
    {
        var bg = new SolidColorBrush(Card.Color);
        var tb = new TextBlock { Text = "🖥 весь ПК", Foreground = BlueHi, FontFamily = Mono, FontSize = 12, FontWeight = FontWeights.Bold };
        var b = new Border { Background = bg, CornerRadius = new CornerRadius(14), Cursor = Cursors.Hand,
            BorderBrush = Blue, BorderThickness = new Thickness(1), Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(16, 0, 0, 0), Child = tb, ToolTip = "Искать по всем дискам компьютера" };
        b.MouseEnter += (s, e) => AC(bg, bg.Color, CardHi.Color);
        b.MouseLeave += (s, e) => AC(bg, bg.Color, Card.Color);
        b.MouseLeftButtonUp += (s, e) =>
        {
            if (searching) { canceled = true; status.Foreground = Sub; status.Text = "останавливаю…"; }
            else StartAll();
        };
        return b;
    }

    static void SelectChip(string key)
    {
        mode = key;
        foreach (var kv in chips)
        {
            bool on = kv.Key == key;
            var b = kv.Value;
            ((SolidColorBrush)b.Background).Color = on ? Blue.Color : Card.Color;
            b.BorderBrush = on ? Blue : Line;
            ((TextBlock)b.Child).Foreground = on ? Brushes.White : Sub;
        }
        if (phWhat != null)
            phWhat.Text = key == "wild" ? "маска: *.jpg  или  photo*" :
                          key == "ext"  ? "расширение: pdf" :
                          key == "exact"? "точное имя: report.docx" :
                                          "часть имени файла";
    }

    static Border WinBtn(string glyph, Color hover)
    {
        var bg = new SolidColorBrush(Colors.Transparent);
        var tb = new TextBlock { Text = glyph, Foreground = Sub, FontFamily = Mono, FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Background = bg, Width = 34, Height = 30, CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0), Child = tb };
        b.MouseEnter += (s, e) => { AC(bg, Colors.Transparent, hover); tb.Foreground = Text; };
        b.MouseLeave += (s, e) => { AC(bg, hover, Colors.Transparent); tb.Foreground = Sub; };
        return b;
    }

    // чёткий PNG иконки (256px) из папки приложения
    static ImageSource LoadIconPng()
    {
        try
        {
            string png = IOPath.Combine(AppDomain.CurrentDomain.BaseDirectory, "finder.png");
            if (File.Exists(png))
            {
                var b = new BitmapImage();
                b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(png); b.EndInit();
                return b;
            }
        }
        catch { }
        return null;
    }

    // маленькая иконка папка+лупа (векторно, запасной вариант)
    static UIElement IconGlyph(double size)
    {
        var c = new Canvas { Width = size, Height = size };
        double k = size / 26.0;
        var folder = new System.Windows.Shapes.Path
        {
            Stroke = Blue, StrokeThickness = 2.2 * k, StrokeLineJoin = PenLineJoin.Round,
            Data = Geometry.Parse(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "M {0},{1} L {2},{1} L {2},{3} L {4},{3} L {4},{5} L {0},{5} Z",
                3 * k, 9 * k, 11 * k, 21 * k, 23 * k, 6 * k))
        };
        c.Children.Add(folder);
        var ring = new Ellipse { Width = 11 * k, Height = 11 * k, Stroke = Text, StrokeThickness = 2.4 * k, Fill = Brushes.Transparent };
        Canvas.SetLeft(ring, 12 * k); Canvas.SetTop(ring, 10 * k); c.Children.Add(ring);
        var handle = new Line { X1 = 21 * k, Y1 = 19 * k, X2 = 25 * k, Y2 = 23 * k,
            Stroke = Text, StrokeThickness = 2.6 * k, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        c.Children.Add(handle);
        return c;
    }

    static Border Spinner()
    {
        var arc = new System.Windows.Shapes.Path { Stroke = BlueHi, StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var fig = new PathFigure { StartPoint = new Point(9, 1) };
        fig.Segments.Add(new ArcSegment(new Point(17, 9), new Size(8, 8), 0, true, SweepDirection.Clockwise, true));
        var geo = new PathGeometry(); geo.Figures.Add(fig); arc.Data = geo;
        spinRot = new RotateTransform(0, 9, 9); arc.RenderTransform = spinRot;
        return new Border { Width = 18, Height = 18, Child = arc, VerticalAlignment = VerticalAlignment.Center };
    }

    static Style ItemStyle()
    {
        var st = new Style(typeof(ListBoxItem));
        st.Setters.Add(new Setter(Control.ForegroundProperty, Text));
        st.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 5, 10, 5)));
        st.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
        var over = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(Control.BackgroundProperty, Card));
        st.Triggers.Add(over);
        var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        sel.Setters.Add(new Setter(Control.BackgroundProperty, CardHi));
        st.Triggers.Add(sel);
        return st;
    }

    // ================= анимации/утилиты =================
    static void StartSpin() { spinRot.BeginAnimation(RotateTransform.AngleProperty,
        new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever }); }
    static void StopSpin() { spinRot.BeginAnimation(RotateTransform.AngleProperty, null); }

    static void Pulse(UIElement el)
    {
        el.BeginAnimation(UIElement.OpacityProperty, null);
        el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase() });
    }
    static void AC(SolidColorBrush br, Color from, Color to)
    { br.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(from, to, TimeSpan.FromMilliseconds(150))); }
    static DoubleAnimation DA(double a, double b, int ms) { return new DoubleAnimation(a, b, TimeSpan.FromMilliseconds(ms)); }

    // формат времени поиска: «3.4 с» до минуты, дальше «м:сс»
    static string Fmt(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return t.TotalSeconds.ToString("0.0") + " с";
        return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
    }

    static bool Wild(string text, string pat)
    {
        int t = 0, p = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pat.Length && (pat[p] == '?' || pat[p] == text[t])) { t++; p++; }
            else if (p < pat.Length && pat[p] == '*') { star = p; mark = t; p++; }
            else if (star != -1) { p = star + 1; mark++; t = mark; }
            else return false;
        }
        while (p < pat.Length && pat[p] == '*') p++;
        return p == pat.Length;
    }

    static SolidColorBrush B(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
}
