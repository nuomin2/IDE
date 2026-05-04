using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using Pytools.Commands;

namespace Pytools.ViewModels
{
    public class OpenedFileViewModel : INotifyPropertyChanged
    {
        private string _fileName = string.Empty;
        private string _filePath = string.Empty;
        private string _content = string.Empty;
        private bool _isDirty;

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public string Content
        {
            get => _content;
            set { _content = value; OnPropertyChanged(); }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set { _isDirty = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private string _windowTitle = "My Python IDE";
        private string _consoleText = "Python 3.12.1 | Console Ready\n";
        private string? _currentRootPath;
        private string _activeFilePath = "请打开文件或文件夹以开始项目";
        private OpenedFileViewModel? _selectedFile;

        public MainViewModel()
        {
            Files = new ObservableCollection<OpenedFileViewModel>();
            OpenFolderCommand = new RelayCommand(_ => ExecuteOpenFolder());
            OpenFileCommand = new RelayCommand(_ => ExecuteOpenFile());
            SaveCommand = new RelayCommand(_ => ExecuteSave(), _ => SelectedFile is not null);
        }

        public string ActiveFilePath
        {
            get => _activeFilePath;
            set { _activeFilePath = value; OnPropertyChanged(); }
        }

        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        public string ConsoleText
        {
            get => _consoleText;
            set { _consoleText = value; OnPropertyChanged(); }
        }

        public string? CurrentRootPath
        {
            get => _currentRootPath;
            set { _currentRootPath = value; OnPropertyChanged(); }
        }

        public ObservableCollection<OpenedFileViewModel> Files { get; }

        public OpenedFileViewModel? SelectedFile
        {
            get => _selectedFile;
            set
            {
                _selectedFile = value;
                OnPropertyChanged();
                ActiveFilePath = value?.FilePath ?? "请打开文件或文件夹以开始项目";
                if (value is not null)
                {
                    WindowTitle = $"My Python IDE - {value.FileName}";
                }
            }
        }

        public ICommand OpenFolderCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand SaveCommand { get; }

        private void ExecuteOpenFolder()
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                if (string.IsNullOrEmpty(dialog.FolderName))
                {
                    ConsoleText += "未选择路径或路径为空\n";
                    return;
                }

                CurrentRootPath = dialog.FolderName;
                ActiveFilePath = dialog.FolderName;
                ConsoleText += $"当前项目路径: {CurrentRootPath}\n";
            }
        }

        private void ExecuteOpenFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Python files (*.py)|*.py|All files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string filePath = openFileDialog.FileName;
                    string? folderPath = Path.GetDirectoryName(filePath);

                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        CurrentRootPath = folderPath;
                    }

                    var existing = FindOpenedFile(filePath);
                    if (existing is null)
                    {
                        existing = new OpenedFileViewModel
                        {
                            FileName = Path.GetFileName(filePath),
                            FilePath = filePath,
                            Content = File.ReadAllText(filePath, Encoding.UTF8),
                            IsDirty = false
                        };
                        Files.Add(existing);
                    }

                    SelectedFile = existing;
                    ConsoleText += $"已打开文件并载入目录: {Path.GetFileName(filePath)}\n";
                }
                catch (Exception ex)
                {
                    ConsoleText += $"操作失败: {ex.Message}\n";
                }
            }
        }

        private void ExecuteSave()
        {
            if (SelectedFile is null)
            {
                return;
            }

            try
            {
                File.WriteAllText(SelectedFile.FilePath, SelectedFile.Content, Encoding.UTF8);
                SelectedFile.IsDirty = false;
                ConsoleText += $"保存成功: {SelectedFile.FilePath}\n";
            }
            catch (Exception ex)
            {
                ConsoleText += $"保存失败: {ex.Message}\n";
            }
        }

        public OpenedFileViewModel? FindOpenedFile(string filePath)
        {
            foreach (var file in Files)
            {
                if (string.Equals(file.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }

            return null;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
