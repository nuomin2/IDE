using Microsoft.Win32;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting; // 必须引用高亮命名空间
using Pytools.ViewModels; // 引用 ViewModel 命名空间
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Pytools.Views
{

    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        public MainWindow()
        {
            InitializeComponent();

            // 初始化并绑定 ViewModel
            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            // 启动时加载 Python 高亮规则
            if (CodeEditor != null)
            {
                CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python");
            }

            // 监听 ViewModel 信号：处理数据变化引起的纯 UI 渲染
            _viewModel.PropertyChanged += (s, e) =>
            {
                // 当根路径改变时，重绘左侧树状目录
                if (e.PropertyName == nameof(MainViewModel.CurrentRootPath))
                {
                    LoadDirectory(_viewModel.CurrentRootPath);
                }
                // 同步更新 AvalonEdit 编辑器内容（规避复杂的直接绑定）
                else if (e.PropertyName == nameof(MainViewModel.CodeContent))
                {
                    if (CodeEditor != null)
                    {
                        CodeEditor.Text = _viewModel.CodeContent;
                    }
                }
                // 当控制台文本更新时，自动滚动到底部
                else if (e.PropertyName == nameof(MainViewModel.ConsoleText))
                {
                    Dispatcher.BeginInvoke(new Action(() => {
                        var scrollViewer = ConsoleOutput.Parent as ScrollViewer;
                        scrollViewer?.ScrollToEnd();
                    }));
                }
            };
        }
        


        /// 遍历文件夹，将文件名显示在左侧的 ProjectTreeView 中 (纯 UI 操作)
        private void LoadDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            ProjectTreeView.Items.Clear();

            TreeViewItem root = new TreeViewItem
            {
                Header = System.IO.Path.GetFileName(path),
                IsExpanded = true
            };

            try
            {
                foreach (string file in Directory.GetFiles(path))
                {
                    root.Items.Add(new TreeViewItem { Header = System.IO.Path.GetFileName(file) });
                }
                ProjectTreeView.Items.Add(root);
            }
            catch (Exception ex)
            {
                _viewModel.ConsoleText += $"加载出错: {ex.Message}\n";
            }
        }


        /// 当用户点击左侧树状列表中的文件项时触发
        private void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = ProjectTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null || _viewModel.CurrentRootPath == null) return;

            if (selectedItem.Items.Count == 0)
            {
                try
                {
                    var headerObj = selectedItem.Header;
                    if (headerObj is not string fileName || string.IsNullOrEmpty(fileName)) return;

                    string filePath = System.IO.Path.Combine(_viewModel.CurrentRootPath!, fileName);

                    if (File.Exists(filePath))
                    {
                        // 读取内容并抛给 ViewModel，触发 UI 自动同步更新（标题栏和编辑器代码）
                        _viewModel.CodeContent = File.ReadAllText(filePath, Encoding.UTF8);
                        _viewModel.WindowTitle = $"My Python IDE - {fileName}";
                        _viewModel.ConsoleText += $"成功读取文件: {fileName}\n";
                    }
                }
                catch (Exception ex)
                {
                    _viewModel.ConsoleText += $"无法读取文件: {ex.Message}\n";
                }
            }
        }
    }
}
      
        
       



    /// <summary>
     /// Interaction logic for MainWindow.xaml
     /// </summary>
