using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock.Layout;
using ICSharpCode.AvalonEdit;
using Pytools.Models;
using Pytools.ViewModels;

namespace Pytools.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly Dictionary<DocumentModel, LayoutDocument> _docToTab = new();
        private bool _isSyncingActive;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new MainViewModel();
            this.DataContext = _viewModel;

            _viewModel.Documents.CollectionChanged += OnDocumentsCollectionChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.TreeRefreshRequested += () =>
            {
                if (!string.IsNullOrEmpty(_viewModel.CurrentRootPath))
                    LoadDirectory(_viewModel.CurrentRootPath);
            };

            _viewModel.CreateStartupPage();
        }

        private void OnDocumentsCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (DocumentModel doc in e.NewItems)
                    AddTab(doc);

            if (e.OldItems != null)
                foreach (DocumentModel doc in e.OldItems)
                    RemoveTab(doc);
        }

        private void AddTab(DocumentModel doc)
        {
            var editor = new TextEditor
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                ShowLineNumbers = true,
                SyntaxHighlighting = ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance.GetDefinition("Python"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = doc.Content
            };

            editor.TextChanged += (s, e) => doc.Content = editor.Text;

            doc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DocumentModel.Content) && editor.Text != doc.Content)
                    editor.Text = doc.Content;
            };

            var layoutDoc = new LayoutDocument
            {
                Title = doc.Title,
                Content = editor
            };

            doc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DocumentModel.Title))
                    layoutDoc.Title = doc.Title;
            };

            layoutDoc.IsActiveChanged += (s, e) =>
            {
                if (_isSyncingActive) return;
                if (layoutDoc.IsActive)
                {
                    _isSyncingActive = true;
                    _viewModel.ActiveDocument = doc;
                    _isSyncingActive = false;
                }
            };

            layoutDoc.Closed += (s, e) =>
            {
                _docToTab.Remove(doc);
                _viewModel.Documents.Remove(doc);
            };

            _docToTab[doc] = layoutDoc;
            DocumentPane.Children.Add(layoutDoc);

            layoutDoc.IsActive = true;
        }

        private void RemoveTab(DocumentModel doc)
        {
            if (!_docToTab.TryGetValue(doc, out var layoutDoc)) return;

            _docToTab.Remove(doc);
            if (DocumentPane.Children.Contains(layoutDoc))
                DocumentPane.Children.Remove(layoutDoc);
        }

        private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentRootPath))
            {
                LoadDirectory(_viewModel.CurrentRootPath);
            }
            else if (e.PropertyName == nameof(MainViewModel.ConsoleText))
            {
                Dispatcher.BeginInvoke(new Action(() => {
                    var scrollViewer = ConsoleOutput.Parent as ScrollViewer;
                    scrollViewer?.ScrollToEnd();
                }));
            }
            else if (e.PropertyName == "ActiveDocument")
            {
                if (_isSyncingActive) return;
                if (_viewModel.ActiveDocument != null
                    && _docToTab.TryGetValue(_viewModel.ActiveDocument, out var target))
                {
                    _isSyncingActive = true;
                    target.IsActive = true;
                    _isSyncingActive = false;
                }
            }
        }

        private void LoadDirectory(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            ProjectTreeView.Items.Clear();

            try
            {
                var rootNode = CreateDirectoryNode(path);
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
                Header = System.IO.Path.GetFileName(path),
                Tag = path
            };

            if (string.IsNullOrEmpty(node.Header.ToString()))
                node.Header = path;

            try
            {
                foreach (string directory in Directory.GetDirectories(path))
                    node.Items.Add(CreateDirectoryNode(directory));

                foreach (string file in Directory.GetFiles(path))
                {
                    node.Items.Add(new TreeViewItem
                    {
                        Header = System.IO.Path.GetFileName(file),
                        Tag = file
                    });
                }
            }
            catch (UnauthorizedAccessException) { }

            return node;
        }

        private void ProjectTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = ProjectTreeView.SelectedItem as TreeViewItem;
            if (selectedItem == null) return;

            string? fullPath = selectedItem.Tag as string;
            if (string.IsNullOrEmpty(fullPath)) return;

            if (File.Exists(fullPath))
                _viewModel.OpenOrActivateDocument(fullPath);
        }
    }
}
