using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting; // 必须引用高亮命名空间
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Pytools
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        // 用于记录当前打开的项目根目录路径，方便拼接文件完整路径
        private string? _currentRootPath;

        public MainWindow()
        {
            InitializeComponent();
            // 启动时加载 Python 高亮规则
            if (CodeEditor != null)
            {
                CodeEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python");
            }
        }

       
        /// <summary>
        /// “打开”按钮点击事件：选择文件夹并刷新左侧树状列表
        /// </summary>
        private void OpenBtn_Click(object sender, RoutedEventArgs e)
        {
            // 使用 .NET 8 建议的文件夹选择对话框
            OpenFolderDialog dialog = new OpenFolderDialog();

            if (dialog.ShowDialog() == true)
            {
                // 先使用局部变量确保编译器能识别非空情况
                string? folder = dialog.FolderName;
                if (string.IsNullOrEmpty(folder))
                {
                    ConsoleOutput.Text = "未选择路径或路径为空\n";
                    return;
                }

                // 1. 记录选中的文件夹路径
                _currentRootPath = folder;

                // 2. 在底部控制台显示当前路径，确认操作成功（使用局部变量 folder）
                ConsoleOutput.Text = $"当前项目路径: {folder}\n";

                // 3. 调用加载目录的方法，把文件显示在左侧 TreeView
                LoadDirectory(folder);
            }
        }

        /// <summary>
        /// 遍历文件夹，将文件名显示在左侧的 ProjectTreeView 中
        /// </summary>
        private void LoadDirectory(string path)
        {
            // 清空现有的列表项，防止多次打开文件夹后内容堆积
            ProjectTreeView.Items.Clear();

            // 创建根节点（显示文件夹名称）
            TreeViewItem root = new TreeViewItem
            {
                Header = System.IO.Path.GetFileName(path),
                IsExpanded = true
            };

            try
            {
                // 获取当前目录下的所有文件并添加到根节点
                foreach (string file in Directory.GetFiles(path))
                {
                    root.Items.Add(new TreeViewItem { Header = System.IO.Path.GetFileName(file) });
                }

                // 将构建好的树形结构添加到 UI 控件中
                ProjectTreeView.Items.Add(root);
            }
            catch (Exception ex)
            {
                // 打印错误信息（如权限问题或路径不存在）
                ConsoleOutput.Text += $"加载出错: {ex.Message}\n";
            }
        }

        /// <summary>
        /// 当用户点击左侧树状列表中的文件项时触发
        /// </summary>
        private void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // 获取当前选中的 TreeView 节点
            var selectedItem = ProjectTreeView.SelectedItem as TreeViewItem;

            // 检查：确保选中了东西，且已经打开了一个文件夹
            if (selectedItem == null || _currentRootPath == null) return;

            // 逻辑判断：如果该节点没有子节点，我们暂时认为它是一个文件
            if (selectedItem.Items.Count == 0)
            {
                try
                {
                    // 获取 Header 并确保它是非空字符串，避免 CS8600/空引用
                    var headerObj = selectedItem.Header;
                    if (headerObj is not string fileName || string.IsNullOrEmpty(fileName))
                    {
                        ConsoleOutput.Text += "选中文件项无效。\n";
                        return;
                    }

                    // 将文件名与根路径拼接，得到文件在硬盘上的真实路径
                    string filePath = System.IO.Path.Combine(_currentRootPath, fileName);

                    // 判断该文件是否真的存在
                    if (File.Exists(filePath))
                    {
                        // 读取文件内容（强制使用 UTF8 编码，防止中文注释显示乱码）
                        string content = File.ReadAllText(filePath, Encoding.UTF8);

                        // 将读取到的文字填入中间的代码编辑区
                        CodeEditor.Text = content;

                        // 控制台输出成功日志
                        ConsoleOutput.Text += $"成功读取文件: {fileName}\n";
                    }
                }
                catch (Exception ex)
                {
                    // 报错处理：例如文件正在被其他程序占用
                    ConsoleOutput.Text += $"无法读取文件: {ex.Message}\n";
                }
            }
        }

        /// <summary>
        /// 当代码编辑区内容改变时触发（目前为空，后续可用于实现自动保存或修改标记）
        /// </summary>
        private void CodeEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 预留位置
        }
    }
}
    
    /// <summary>
     /// Interaction logic for MainWindow.xaml
     /// </summary>
