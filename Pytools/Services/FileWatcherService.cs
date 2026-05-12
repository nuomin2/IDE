using System;
using System.IO;

namespace Pytools.Services
{
    public class FileWatcherService : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private System.Timers.Timer? _debounceTimer;
        private readonly string _filter;
        private readonly int _debounceMs;

        public event Action? FilesChanged;

        public FileWatcherService(string filter = "*.py", int debounceMs = 200)
        {
            _filter = filter;
            _debounceMs = debounceMs;
        }

        public void Start(string path)
        {
            if (!Directory.Exists(path)) return;

            Stop();

            _watcher = new FileSystemWatcher(path)
            {
                Filter = _filter,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };

            _debounceTimer = new System.Timers.Timer(_debounceMs) { AutoReset = false };
            _debounceTimer.Elapsed += OnDebounceElapsed;

            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnFileChanged;

            _watcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            if (_debounceTimer != null)
            {
                _debounceTimer.Stop();
                _debounceTimer.Dispose();
                _debounceTimer = null;
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            _debounceTimer?.Stop();
            _debounceTimer?.Start();
        }

        private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.Invoke(() => FilesChanged?.Invoke());
        }

        public void Dispose()
        {
            Stop();
            FilesChanged = null;
        }
    }
}
