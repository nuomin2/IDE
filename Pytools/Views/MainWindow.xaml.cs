using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Pytools.ViewModels;
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Pytools.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.CurrentRootPath))
                {
                    LoadDirectory(_viewModel.CurrentRootPath);
                }
                else if (e.PropertyName == nameof(MainViewModel.ConsoleText))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ConsoleOutput.Parent is ScrollViewer scrollViewer)
                        {
                            scrollViewer.ScrollToEnd();
                        }
                    }));
                }
            };
        }

        private void Editor_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not TextEditor editor || editor.DataContext is not OpenedFileViewModel vm)
            {
                return;
            }

            editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Python");
            if (!string.Equals(editor.Text, vm.Content, StringComparison.Ordinal))
            {
                editor.Text = vm.Content;
            }
        }

        private void Editor_TextChanged(object sender, EventArgs e)
        {
            if (sender is not TextEditor editor || editor.DataContext is not OpenedFileViewModel vm)
            {
                return;
            }

            if (!string.Equals(vm.Content, editor.Text, StringComparison.Ordinal))
            {
                vm.Content = editor.Text;
                vm.IsDirty = true;
            }
        }

        private void LoadDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            ProjectTreeView.Items.Clear();

            try
            {
                TreeViewItem rootNode = CreateDirectoryNode(path);
                rootNode.IsExpanded = true;
                ProjectTreeView.Items.Add(rootNode);
            }
            catch (Exception ex)
            {
                _viewModel.ConsoleText += $"加载目录树出错: {ex.Message}\n";
            }
        }

        private TreeViewItem CreateDirectoryNode(string path)
        {
            var node = new TreeViewItem
            {
                Header = Path.GetFileName(path),
                Tag = path
            };

            if (string.IsNullOrEmpty(node.Header?.ToString()))
            {
                node.Header = path;
            }

            try
            {
                foreach (string directory in Directory.GetDirectories(path))
                {
                    node.Items.Add(CreateDirectoryNode(directory));
                }

                foreach (string file in Directory.GetFiles(path))
                {
                    node.Items.Add(new TreeViewItem
                    {
                        Header = Path.GetFileName(file),
                        Tag = file
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
            }

            return node;
        }

        private void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (ProjectTreeView.SelectedItem is not TreeViewItem selectedItem)
            {
                return;
            }

            string? fullPath = selectedItem.Tag as string;
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return;
            }

            try
            {
                var opened = _viewModel.FindOpenedFile(fullPath);
                if (opened is null)
                {
                    opened = new OpenedFileViewModel
                    {
                        FileName = Path.GetFileName(fullPath),
                        FilePath = fullPath,
                        Content = File.ReadAllText(fullPath, Encoding.UTF8),
                        IsDirty = false
                    };
                    _viewModel.Files.Add(opened);
                    _viewModel.ConsoleText += $"成功读取文件: {fullPath}\n";
                }

                _viewModel.SelectedFile = opened;
            }
            catch (Exception ex)
            {
                _viewModel.ConsoleText += $"无法读取文件: {ex.Message}\n";
            }
        }
    }
}
