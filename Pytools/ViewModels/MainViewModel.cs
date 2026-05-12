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
        private string _consoleText = "Python 3.12.1 | Console Ready\n";
        private string? _currentRootPath;
        private DocumentModel? _activeDocument;
        private readonly FileWatcherService _fileWatcher;

        public event Action? TreeRefreshRequested;

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

        public string ConsoleText
        {
            get => _consoleText;
            set { _consoleText = value; OnPropertyChanged(); }
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

        public MainViewModel()
        {
            OpenFolderCommand = new RelayCommand(_ => ExecuteOpenFolder());
            OpenFileCommand = new RelayCommand(_ => ExecuteOpenFile());
            SaveCommand = new RelayCommand(_ => ExecuteSave());
            NewCommand = new RelayCommand(_ => ExecuteNew());

            _fileWatcher = new FileWatcherService("*.py", 200);
            _fileWatcher.FilesChanged += () => TreeRefreshRequested?.Invoke();
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
                ConsoleText += $"已打开: {filePath}\n";
            }
            catch (Exception ex)
            {
                ConsoleText += $"无法打开文件: {ex.Message}\n";
            }
        }

        private void ExecuteOpenFolder()
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(dialog.FolderName))
                {
                    ConsoleText += "未选择路径或路径为空\n";
                    return;
                }
                CurrentRootPath = dialog.FolderName;
                ConsoleText += $"当前项目路径: {CurrentRootPath}\n";
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
                ConsoleText += $"文件已保存: {filePath}\n";
            }
            catch (Exception ex)
            {
                ConsoleText += $"保存失败: {ex.Message}\n";
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
            ConsoleText += $"已创建新文件: {title}\n";
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
