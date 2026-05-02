using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using Pytools.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Pytools.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _windowTitle = "My Python IDE";
        private string _consoleText = "Python 3.12.1 | Console Ready\n";
        private string? _currentRootPath;
        private string _codeContent = "";



        // 绑定到窗口标题栏
        public string WindowTitle
        {
            get => _windowTitle;
            // 当窗口标题被设置时，更新字段并触发属性更改通知以刷新绑定到 UI 的标题
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        // 绑定到控制台输出
        public string ConsoleText
        {
            get => _consoleText;
            set { _consoleText = value; OnPropertyChanged(); }
        }

        // 绑定到代码编辑器内容
        public string CodeContent
        {
            get => _codeContent;
            set { _codeContent = value; OnPropertyChanged(); }
        }

        // 核心信号：当此路径改变时，通知 View 刷新树状列表
        public string? CurrentRootPath
        {
            get => _currentRootPath;
            set { _currentRootPath = value; OnPropertyChanged(); }
        }

        public ICommand OpenFolderCommand { get; }  ///只读属性，类型为 ICommand，命令模式接口，用于绑定 UI 中的打开文件夹操作
        public ICommand OpenFileCommand { get; }  ///只读属性，类型为 ICommand，命令模式接口，用于绑定 UI 中的打开文件操作

        public MainViewModel()
        {
            OpenFolderCommand = new RelayCommand(_ => ExecuteOpenFolder());
            OpenFileCommand = new RelayCommand(_ => ExecuteOpenFile());
        }

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

                CurrentRootPath = dialog.FolderName; // 触发 UI 刷新树
                ConsoleText += $"当前项目路径: {CurrentRootPath}\n";
            }
        }

        private void ExecuteOpenFile()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Python files (*.py)|*.py|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string filePath = openFileDialog.FileName;
                    string? folderPath = Path.GetDirectoryName(filePath);

                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        CurrentRootPath = folderPath; // 触发 UI 刷新树
                    }

                    // 读取内容并更新状态
                    CodeContent = File.ReadAllText(filePath, Encoding.UTF8);
                    WindowTitle = $"My Python IDE - {Path.GetFileName(filePath)}";
                    ConsoleText += $"已打开文件并载入目录: {Path.GetFileName(filePath)}\n";
                }
                catch (Exception ex)
                {
                    ConsoleText += $"操作失败: {ex.Message}\n";
                }
            }
        }
        //事件和委托
        public event PropertyChangedEventHandler? PropertyChanged;

        //PropertyChangedEventHandler是委托类型，表示属性更改事件的处理方法。
        //当属性值发生变化时，调用OnPropertyChanged方法触发事件，通知UI更新绑定到该属性的元素。

        // CallerMemberName特性允许在调用OnPropertyChanged方法时自动获取调用者的成员名称（属性名称），从而简化代码并减少错误。

        //OnPropertyChanged方法接受一个可选的字符串参数name，表示发生变化的属性名称。
        //如果调用时未提供该参数，编译器会自动将调用者的成员名称传递给它。
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /*总结!!!*/
        // PropertyChanged 是一个事件，类型为 PropertyChangedEventHandler。
        // 当属性值发生变化时，调用 OnPropertyChanged 方法触发该事件，通知 UI 更新绑定到该属性的元素。
        //


    }
}
