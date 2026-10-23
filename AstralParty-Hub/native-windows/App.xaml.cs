using System.IO;
using System.Threading;
using System.Windows;
using XingShu.Native.Services;

namespace XingShu.Native;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var test = Array.IndexOf(e.Args, "--self-test");
        if (test >= 0)
        {
            try { await NativeSelfTest.Run(test + 1 < e.Args.Length ? e.Args[test + 1] : Path.Combine(Path.GetTempPath(), "xingshu-native-test")); Shutdown(0); }
            catch (Exception ex) { var folder = test + 1 < e.Args.Length ? e.Args[test + 1] : Path.GetTempPath(); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "failure.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        instance = new Mutex(true, @"Local\XingShuHub.Native.v1", out var created);
        if (!created) { MessageBox.Show("星枢原生版已经运行，请从任务栏打开已有窗口。", "星枢 Hub"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => {
            Directory.CreateDirectory(Preferences.Folder); File.AppendAllText(Path.Combine(Preferences.Folder, "errors.log"), DateTimeOffset.Now + " " + args.Exception + "\n");
            MessageBox.Show("操作未完成：" + args.Exception.Message, "星枢 Hub"); args.Handled = true;
        };
        try
        {
            var catalog = await Task.Run(() => new CatalogStore());
            var window = new MainWindow(catalog, Preferences.Load()); MainWindow = window; window.Show();
        }
        catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "星枢 Hub"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
