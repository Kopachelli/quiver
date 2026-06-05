using System.IO;
using System.Windows.Threading;

namespace SkillzWin.Services;

/// <summary>
/// Watches skill/config roots for changes and fires a single coalesced callback. Windows port of
/// the macOS FSEvents watcher (R3): one <see cref="FileSystemWatcher"/> per existing root,
/// recursive, all sharing one 300 ms <see cref="DispatcherTimer"/> debounce; recreates everything
/// on a buffer-overflow <c>Error</c>.
/// </summary>
public sealed class CatalogFileWatcher : IDisposable
{
    private readonly Action _onChange;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _debounce;
    private IReadOnlyList<string> _roots = Array.Empty<string>();

    public CatalogFileWatcher(Action onChange)
    {
        _onChange = onChange;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); _onChange(); };
    }

    public void Start(IReadOnlyList<string> roots)
    {
        Stop();
        _roots = roots;
        foreach (var root in roots) TryCreate(root);
    }

    public void Stop()
    {
        foreach (var w in _watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch { /* ignore */ }
        }
        _watchers.Clear();
        _debounce.Stop();
    }

    public void Dispose() => Stop();

    private void TryCreate(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch { /* root may have vanished; ignore */ }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Bounce();
    private void OnRenamed(object sender, RenamedEventArgs e) => Bounce();

    private void Bounce()
    {
        // FileSystemWatcher events arrive on a thread-pool thread; the timer lives on the UI thread.
        if (_debounce.Dispatcher.CheckAccess())
        {
            _debounce.Stop();
            _debounce.Start();
        }
        else
        {
            _debounce.Dispatcher.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
        }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        _debounce.Dispatcher.BeginInvoke(() =>
        {
            var roots = _roots;
            Stop();
            Start(roots);
        });
    }
}
