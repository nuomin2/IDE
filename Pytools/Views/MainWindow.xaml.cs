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



        /// 遍历文件夹，支持多层级递归 (纯 UI 操作)
        private void LoadDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            ProjectTreeView.Items.Clear();

            try
            {
                // 从根节点开始构建树
                TreeViewItem rootNode = CreateDirectoryNode(path);
                rootNode.IsExpanded = true; // 默认展开根目录
                ProjectTreeView.Items.Add(rootNode);
            }
            catch (Exception ex)
            {
                _viewModel.ConsoleText += $"加载目录树出错: {ex.Message}\n";
            }
        }

        /// 递归生成目录树节点的核心方法
        private TreeViewItem CreateDirectoryNode(string path)
        {
            var node = new TreeViewItem
            {
                Header = System.IO.Path.GetFileName(path),
                Tag = path // 重点：将该文件夹的完整绝对路径存入 Tag
            };

            // 如果选择了磁盘根目录（如 "C:\"），GetFileName 会返回空，此时直接用路径作为 Header
            if (string.IsNullOrEmpty(node.Header.ToString()))
            {
                node.Header = path;
            }

            try
            {
                // 1. 先遍历并添加子文件夹（递归调用）
                foreach (string directory in Directory.GetDirectories(path))
                {
                    node.Items.Add(CreateDirectoryNode(directory));
                }

                // 2. 再遍历并添加当前目录下的文件
                foreach (string file in Directory.GetFiles(path))
                {
                    node.Items.Add(new TreeViewItem
                    {
                        Header = System.IO.Path.GetFileName(file),
                        Tag = file // 重点：将文件的完整绝对路径存入 Tag
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略系统隐藏文件夹或没有权限访问的文件夹，防止程序崩溃
            }

            return node;
        }

        /// 当用户点击左侧树状列表中的项时触发
        private void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = ProjectTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null) return;

            // 直接从 Tag 中取出完整路径，不再需要通过 CurrentRootPath 去拼凑
            string? fullPath = selectedItem.Tag as string;

            if (string.IsNullOrEmpty(fullPath)) return;

            // 检查选中的是否是真实存在的文件（排除点击文件夹的情况）
            if (File.Exists(fullPath))
            {
                try
                {
                    // 读取内容并抛给 ViewModel，触发 UI 自动同步更新
                    _viewModel.CodeContent = File.ReadAllText(fullPath, Encoding.UTF8);
                    _viewModel.WindowTitle = $"My Python IDE - {System.IO.Path.GetFileName(fullPath)}";
                    _viewModel.ActiveFilePath = fullPath; // 更新当前活动文件路径
                    _viewModel.ConsoleText += $"成功读取文件: {fullPath}\n";
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
