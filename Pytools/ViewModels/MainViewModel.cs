using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using Pytools.Commands;
using Pytools.Models;
using Pytools.Services;

namespace Pytools.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string? _currentRootPath;
        private DocumentModel? _activeDocument;
        private readonly FileWatcherService _fileWatcher;
        private readonly PythonExecutionService _pythonService;
        private int _executionCount = 1;

        public event Action? TreeRefreshRequested;
        public event Action? ConsoleActivateRequested;

        public ObservableCollection<string> ConsoleLines { get; } = new();
        public ObservableCollection<VariableEntry> VariableList { get; } = new();

        public ObservableCollection<DocumentModel> Documents { get; } = new();

        public DocumentModel? ActiveDocument
        {
            get => _activeDocument;
            set
            {
                if (_activeDocument == value) return;

                if (_activeDocument != null)
                    _activeDocument.PropertyChanged -= OnActiveDocumentPropertyChanged;

                _activeDocument = value;

                if (_activeDocument != null)
                    _activeDocument.PropertyChanged += OnActiveDocumentPropertyChanged;

                OnPropertyChanged();
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(CodeContent));
                OnPropertyChanged(nameof(ActiveFilePath));
            }
        }

        public string WindowTitle =>
            ActiveDocument is { } doc
                ? $"My Python IDE - {doc.Title}"
                : "My Python IDE";

        public string CodeContent
        {
            get => ActiveDocument?.Content ?? "";
            set
            {
                if (ActiveDocument != null)
                    ActiveDocument.Content = value;
            }
        }

        public string ActiveFilePath
        {
            get
            {
                if (ActiveDocument == null)
                    return "请打开文件或文件夹以开始项目";
                return string.IsNullOrEmpty(ActiveDocument.FilePath)
                    ? ActiveDocument.Title
                    : ActiveDocument.FilePath;
            }
        }

        public string? CurrentRootPath
        {
            get => _currentRootPath;
            set
            {
                if (_currentRootPath == value) return;
                _currentRootPath = value;
                OnPropertyChanged();

                if (!string.IsNullOrEmpty(value))
                    _fileWatcher.Start(value);
                else
                    _fileWatcher.Stop();
            }
        }

        public ICommand OpenFolderCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand RunCommand { get; }

        public MainViewModel()
        {
            OpenFolderCommand = new RelayCommand(_ => ExecuteOpenFolder());
            OpenFileCommand = new RelayCommand(_ => ExecuteOpenFile());
            SaveCommand = new RelayCommand(_ => ExecuteSave());
            NewCommand = new RelayCommand(_ => ExecuteNew());
            RunCommand = new RelayCommand(_ => ExecuteRun());

            _fileWatcher = new FileWatcherService("*.py", 200);
            _fileWatcher.FilesChanged += () => TreeRefreshRequested?.Invoke();

            _pythonService = new PythonExecutionService();
            _pythonService.LineReceived += line =>
                System.Windows.Application.Current.Dispatcher.Invoke(() => ConsoleLines.Add(line));
            _pythonService.ExecutionFinished += () =>
                System.Windows.Application.Current.Dispatcher.Invoke(() => _executionCount++);
            _pythonService.VariablesReceived += vars =>
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var v in vars)
                        VariableList.Add(v);
                });

            ConsoleLines.Add("Python 3.12.1 | Console Ready");
        }

        public void CreateStartupPage()
        {
            var startPage = new DocumentModel
            {
                Title = "起始页",
                Content = "Welcome",
                FilePath = ""
            };
            Documents.Add(startPage);
            ActiveDocument = startPage;
        }

        public void OpenOrActivateDocument(string filePath)
        {
            var existing = Documents.FirstOrDefault(d => d.FilePath == filePath);
            if (existing != null)
            {
                ActiveDocument = existing;
                return;
            }

            try
            {
                var content = File.ReadAllText(filePath, Encoding.UTF8);
                var doc = new DocumentModel
                {
                    Title = Path.GetFileName(filePath),
                    Content = content,
                    FilePath = filePath
                };
                Documents.Add(doc);
                ActiveDocument = doc;
            }
            catch (Exception ex)
            {
                ConsoleLines.Add($"无法打开文件: {ex.Message}");
            }
        }

        private void ExecuteOpenFolder()
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(dialog.FolderName))
                    return;
                CurrentRootPath = dialog.FolderName;
            }
        }

        private void ExecuteOpenFile()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Python files (*.py)|*.py|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            string filePath = dialog.FileName;
            string? folderPath = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(folderPath))
                CurrentRootPath = folderPath;

            OpenOrActivateDocument(filePath);
        }

        private void ExecuteSave()
        {
            if (ActiveDocument == null) return;

            var filePath = ActiveDocument.FilePath;

            if (string.IsNullOrEmpty(filePath)
                || Directory.Exists(filePath)
                || !File.Exists(filePath))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Python files (*.py)|*.py|All files (*.*)|*.*",
                    DefaultExt = ".py"
                };
                if (dialog.ShowDialog() == true)
                {
                    filePath = dialog.FileName;
                    ActiveDocument.FilePath = filePath;
                    ActiveDocument.Title = Path.GetFileName(filePath);
                }
                else return;
            }

            try
            {
                File.WriteAllText(filePath, ActiveDocument.Content, Encoding.UTF8);
                ConsoleLines.Add($"文件已保存: {filePath}");
            }
            catch (Exception ex)
            {
                ConsoleLines.Add($"保存失败: {ex.Message}");
            }
        }

        private void ExecuteNew()
        {
            var count = Documents.Count(d => d.Title.StartsWith("未命名"));
            var title = count == 0 ? "未命名.py" : $"未命名{count + 1}.py";

            var doc = new DocumentModel
            {
                Title = title,
                Content = "",
                FilePath = ""
            };
            Documents.Add(doc);
            ActiveDocument = doc;
            ConsoleLines.Add($"已创建新文件: {title}");
        }

        private void ExecuteRun()
        {
            if (ActiveDocument == null) return;

            if (string.IsNullOrEmpty(ActiveDocument.FilePath)
                || Directory.Exists(ActiveDocument.FilePath)
                || !File.Exists(ActiveDocument.FilePath))
            {
                ExecuteSave();
                if (string.IsNullOrEmpty(ActiveDocument.FilePath)
                    || !File.Exists(ActiveDocument.FilePath))
                {
                    ConsoleLines.Add("运行中止：请先保存文件");
                    return;
                }
            }

            try
            {
                File.WriteAllText(ActiveDocument.FilePath, ActiveDocument.Content, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ConsoleLines.Add($"保存失败: {ex.Message}");
                return;
            }

            ConsoleLines.Add("");
            ConsoleLines.Add($"In [{_executionCount}]: %runfile {ActiveDocument.FilePath}");

            VariableList.Clear();

            ConsoleActivateRequested?.Invoke();
            _pythonService.Execute(ActiveDocument.FilePath);
        }

        private void OnActiveDocumentPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(DocumentModel.Title):
                    OnPropertyChanged(nameof(WindowTitle));
                    break;
                case nameof(DocumentModel.Content):
                    OnPropertyChanged(nameof(CodeContent));
                    break;
                case nameof(DocumentModel.FilePath):
                    OnPropertyChanged(nameof(ActiveFilePath));
                    break;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
