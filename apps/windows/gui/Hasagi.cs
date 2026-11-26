using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

using System.Windows.Input;
using System.Windows.Markup;

using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

internal sealed class Hasagi
{
    private static readonly Assembly Assembly = typeof(Hasagi).Assembly;
    private static readonly string[] Languages = { "简体中文", "繁體中文", "English" };
    private static readonly Dictionary<string, string[]> Copy = new Dictionary<string, string[]> {
        { "pick", new[] { "选择图像目录", "選擇影像資料夾", "Select image folder" } },
        { "hint", new[] { "或将文件夹拖放到这里", "或將資料夾拖曳至這裡", "or drop a folder here" } },
        { "input", new[] { "输入目录", "輸入資料夾", "Input folder" } },
        { "output", new[] { "输出表格", "輸出表格", "Output files" } },
        { "browse", new[] { "浏览…", "瀏覽…", "Browse…" } },
        { "start", new[] { "开始分析", "開始分析", "Start analysis" } },
        { "console", new[] { "控制台", "主控台", "Console" } },
        { "ready", new[] { "请选择输入目录。", "請選擇輸入資料夾。", "Select an input folder." } },
        { "folder", new[] { "已选择目录：{0}", "已選擇資料夾：{0}", "Folder selected: {0}" } },
        { "scanning", new[] { "正在扫描图像…", "正在掃描影像…", "Scanning images…" } },
        { "count", new[] { "找到 {0} 张图像。", "找到 {0} 張影像。", "Images found: {0}." } },
        { "stop", new[] { "停止", "停止", "Stop" } },
        { "stopping", new[] { "正在保存已完成的结果…", "正在儲存已完成的結果…", "Saving completed results…" } },
        { "stopped", new[] { "已停止扫描。", "已停止掃描。", "Scan stopped." } },
        { "progress", new[] { "已处理 {0}", "已處理 {0}", "Processed {0}" } },
        { "done", new[] { "分析完成，已保存：{0}", "分析完成，已儲存：{0}", "Analysis complete. Saved: {0}" } },
        { "partial", new[] { "已停止，部分结果已保存：{0}", "已停止，部分結果已儲存：{0}", "Stopped. Partial results saved: {0}" } },
        { "failed", new[] { "处理失败的图像：{0}", "處理失敗的影像：{0}", "Images with errors: {0}" } },
        { "error", new[] { "分析失败：{0}", "分析失敗：{0}", "Analysis failed: {0}" } },
        { "language", new[] { "语言", "語言", "Language" } },
        { "expand", new[] { "展开控制台", "展開主控台", "Expand console" } },
        { "collapse", new[] { "收起控制台", "收合主控台", "Collapse console" } }
    };
    private static readonly HashSet<string> Extensions = new HashSet<string>(
        new[] { ".png", ".bmp", ".tif", ".tiff", ".jpeg", ".jpg" }, StringComparer.OrdinalIgnoreCase);

    internal readonly Window Window;

    private readonly List<KeyValuePair<string, object>> logs = new List<KeyValuePair<string, object>>();
    private string folder;
    private string outputStem;
    private CancellationTokenSource cancellation;
    private int language;
    private bool busy;

    internal Hasagi()
    {
        using (var stream = Assembly.GetManifestResourceStream("window.xaml"))
            Window = (Window)XamlReader.Load(stream);
        var painting = ReadImage("ink.png");
        // Select the shallow strip containing the face without stretching the artwork.
        int cropHeight = (int)Math.Round(painting.PixelWidth * 90 / (Window.Width - 24));
        C<Image>("Painting").Source = new CroppedBitmap(painting, new Int32Rect(0,
            (int)((painting.PixelHeight - cropHeight) * 0.24), painting.PixelWidth, cropHeight));
        C<Image>("Wordmark").Source = new CroppedBitmap(ReadImage("wordmark.png"), new Int32Rect(104,122,2018,525));
        var languages = C<ComboBox>("Language");
        languages.ItemsSource = Languages;
        C<Border>("LanguageGlobe").MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) {
            languages.Focus();
            languages.IsDropDownOpen = true;
            e.Handled = true;
        };
        languages.SelectionChanged += delegate { if (languages.SelectedIndex >= 0) SetLanguage(languages.SelectedIndex); };
        var drop = C<Border>("DropZone");
        drop.MouseLeftButtonUp += delegate { ChooseFolder(drop, new RoutedEventArgs()); };
        drop.KeyDown += delegate(object sender, KeyEventArgs e) {
            if (e.Key == Key.Space || e.Key == Key.Enter) {
                e.Handled = true;
                ChooseFolder(sender, new RoutedEventArgs());
            }
        };
        C<Button>("Browse").Click += ChooseFolder;
        C<Button>("ChooseOutput").Click += ChooseOutput;
        Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
            if (busy) { e.Cancel=true; RequestStop(); }
        };
        drop.PreviewDragOver += delegate(object sender, DragEventArgs e) {
            e.Effects = DroppedFolder(e.Data) == null ? DragDropEffects.None : DragDropEffects.Copy;
            e.Handled = true;
        };
        drop.Drop += delegate(object sender, DragEventArgs e) {
            string path = DroppedFolder(e.Data);
            if (path != null) SelectFolder(path);
            e.Handled = true;
        };
        C<Button>("Analyze").Click += ScanFolder;
        var toggle = C<Expander>("Toggle");
        toggle.Expanded += delegate { RenderLog(); };
        toggle.Collapsed += delegate { RenderLog(); };
        Record("ready");
        languages.SelectedIndex = AppLanguage.Load();
        languages.SelectionChanged += delegate { if (languages.SelectedIndex >= 0) AppLanguage.Save(languages.SelectedIndex); };
    }

    private T C<T>(string name) where T : FrameworkElement { return (T)Window.FindName(name); }
    private string T(string key) { return Copy[key][language]; }

    private static BitmapSource ReadImage(string name)
    {
        using (var stream = Assembly.GetManifestResourceStream(name)) {
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            image.Freeze();
            return image;
        }
    }

    private void SetLanguage(int index)
    {
        language = index;
        Window.Language = XmlLanguage.GetLanguage(new[] { "zh-CN", "zh-TW", "en-US" }[index]);
        C<TextBlock>("PickText").Text = T("pick");
        C<TextBlock>("DropHint").Text = T("hint");
        C<Label>("InputLabel").Content = T("input");
        C<Label>("OutputLabel").Content = T("output");
        C<Button>("Browse").Content = T("browse");
        C<Button>("Analyze").Content = T(busy ? "stop" : "start");
        C<Button>("ChooseOutput").ToolTip = T("output");
        AutomationProperties.SetName(C<ComboBox>("Language"), T("language"));
        AutomationProperties.SetName(C<Border>("DropZone"), T("pick"));
        RenderLog();
    }

    private void Record(string key, object value = null)
    {
        logs.Add(new KeyValuePair<string, object>(key, value));
        RenderLog();
    }

    private string FormatLine(KeyValuePair<string, object> entry)
    {
        return string.Format(CultureInfo.CurrentCulture, T(entry.Key), entry.Value);
    }

    private void RenderLog()
    {
        var history = C<TextBox>("History");
        var toggle = C<Expander>("Toggle");
        bool expanded = toggle.IsExpanded;
        string latest = FormatLine(logs.Last());
        C<TextBlock>("Latest").Text = expanded ? T("console") : latest;
        toggle.ToolTip = expanded ? T("collapse") : latest;
        AutomationProperties.SetName(toggle, T(expanded ? "collapse" : "expand"));
        history.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (!expanded) return;
        history.Text = string.Join(Environment.NewLine, logs.Select(line => "> " + FormatLine(line)));
        history.ScrollToEnd();
    }

    private static string DroppedFolder(IDataObject data)
    {
        var paths = data.GetData(DataFormats.FileDrop) as string[];
        return paths != null && paths.Length == 1 && Directory.Exists(paths[0]) ? paths[0] : null;
    }

    private void ChooseFolder(object sender, RoutedEventArgs args)
    {
        if (busy) return;
        using (var dialog = new Forms.FolderBrowserDialog { Description = T("pick"), SelectedPath = folder ?? "", ShowNewFolderButton = false })
            if (dialog.ShowDialog(new DialogOwner(Window)) == Forms.DialogResult.OK) SelectFolder(dialog.SelectedPath);
    }

    private sealed class DialogOwner : Forms.IWin32Window
    {
        public IntPtr Handle { get; private set; }
        internal DialogOwner(Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
    }

    private void SelectFolder(string path)
    {
        folder = path;
        C<TextBox>("InputPath").Text = path;
        SetOutput(Path.Combine(path,"HASAGI_results"));
        C<TextBox>("InputPath").ToolTip = path;
        C<Grid>("Fields").Visibility = Visibility.Visible;
        C<Border>("DropZone").Visibility = Visibility.Collapsed;
        Record("folder", path);
    }

    private void SetOutput(string stem)
    {
        outputStem=stem;
        C<TextBox>("OutputPath").Text=stem+".csv / .xlsx";
        C<TextBox>("OutputPath").ToolTip=stem+".csv"+Environment.NewLine+stem+".xlsx";
    }

    private void ChooseOutput(object sender, RoutedEventArgs args)
    {
        var dialog=new Microsoft.Win32.SaveFileDialog { Filter="CSV + Excel (*.csv)|*.csv", DefaultExt=".csv",
            InitialDirectory=Path.GetDirectoryName(outputStem), FileName=Path.GetFileName(outputStem)+".csv", OverwritePrompt=false };
        if (dialog.ShowDialog(Window)==true) SetOutput(Path.Combine(Path.GetDirectoryName(dialog.FileName),Path.GetFileNameWithoutExtension(dialog.FileName)));
    }

    private void RequestStop()
    {
        if(cancellation==null || cancellation.IsCancellationRequested) return;
        cancellation.Cancel();
        C<Button>("Analyze").IsEnabled=false;
        Record("stopping");
    }

    private void Notify(string key,object value)
    {
        Window.Dispatcher.Invoke(new Action(()=> {
            if(key=="progress" && logs.Count>0 && logs.Last().Key=="progress") logs.RemoveAt(logs.Count-1);
            Record(key,value);
        }));
    }

    private IEnumerable<object[]> AnalyzeFiles(string[] paths,string prefix,CpuModel layers,CpuModel grades,
        CancellationToken token, Action<int,int> processed)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        int total=0, failed=0;
        foreach(string path in paths) {
            if(token.IsCancellationRequested) break;
            var row=Analysis.Process(path,prefix,layers,grades);
            if((string)row[8]!="ok") failed++;
            total++;
            yield return row;
            if(clock.ElapsedMilliseconds>=400 || total==paths.Length) {
                Notify("progress",total+" / "+paths.Length); clock.Restart();
            }
        }
        processed(total,failed);
    }

    private void RunBatch(string input,string stem,CancellationToken token,int reportLanguage)
    {
        string prefix=Path.GetFullPath(input).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var files=Directory.EnumerateFiles(input,"*",SearchOption.AllDirectories)
            .TakeWhile(x=>!token.IsCancellationRequested)
            .Where(x=>Extensions.Contains(Path.GetExtension(x))).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
        token.ThrowIfCancellationRequested();
        Notify("count",files.Length);
        if(files.Length==0) return;
        int processed=0, failed=0;
        string final;
        using(var layers=new CpuModel("layers_fp32.onnx"))
        using(var grades=new CpuModel("discernibility_fp32.onnx")) {
            var rows=AnalyzeFiles(files,prefix,layers,grades,token,(n,e)=>{processed=n;failed=e;});
            final=Report.SavePair(stem,rows,()=>processed<files.Length,reportLanguage);
        }
        if(failed>0) Notify("failed",failed);
        Notify(processed<files.Length?"partial":"done",final+".csv / .xlsx");
    }

    private async void ScanFolder(object sender, RoutedEventArgs args)
    {
        if(busy) {RequestStop();return;}
        if(folder==null) return;
        busy = true;
        cancellation=new CancellationTokenSource();
        C<Button>("Browse").IsEnabled=C<Button>("ChooseOutput").IsEnabled=false;
        C<Button>("Analyze").Content=T("stop");
        Record("scanning");
        try {
            int reportLanguage = language;
            await Task.Run(()=>RunBatch(folder,outputStem,cancellation.Token,reportLanguage));
        } catch (OperationCanceledException) { Record("stopped"); }
        catch (Exception ex) { Record("error", ex.Message); }
        finally {
            busy = false;
            cancellation.Dispose(); cancellation=null;
            C<Button>("Analyze").Content=T("start");
            C<Button>("Analyze").IsEnabled=C<Button>("Browse").IsEnabled=C<Button>("ChooseOutput").IsEnabled=true;
        }
    }

    [STAThread] private static void Main()
    {
        var app = new Application();
        app.Run(new Hasagi().Window);
    }
}
