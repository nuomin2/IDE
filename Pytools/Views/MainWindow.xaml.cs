using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using AvalonDock.Layout;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MaterialDesignThemes.Wpf;
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

            _viewModel.ConsoleLines.CollectionChanged += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ConsoleListBox.Items.Count > 0)
                    {
                        if (VisualTreeHelper.GetChildrenCount(ConsoleListBox) > 0)
                        {
                            var border = VisualTreeHelper.GetChild(ConsoleListBox, 0) as Decorator;
                            var scrollViewer = border?.Child as ScrollViewer;
                            scrollViewer?.ScrollToBottom();
                        }
                    }
                }), DispatcherPriority.Background);
            };

            _viewModel.ConsoleActivateRequested += () =>
            {
                ConsoleAnchorable.IsActive = true;
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

        private static IHighlightingDefinition? _vsCodeLightHighlighting;

        private static IHighlightingDefinition GetVSCodeLightHighlighting()
        {
            if (_vsCodeLightHighlighting != null) return _vsCodeLightHighlighting;

            string xshd = @"<?xml version=""1.0""?>
    <SyntaxDefinition name=""PythonVSCodeLight"" extensions="".py"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
        <Color name=""Comment"" foreground=""#008000"" />
        <Color name=""String"" foreground=""#A31515"" />
        <Color name=""Keyword"" foreground=""#0000FF"" />
        <Color name=""ControlKeyword"" foreground=""#AF00DB"" />
        <Color name=""BuiltIn"" foreground=""#2B91AF"" />
        <Color name=""Number"" foreground=""#098658"" />
        <RuleSet ignoreCase=""false"">
            <Span color=""Comment"" begin=""#"" />
            <Span color=""String"" multiline=""true"">
                <Begin>'''</Begin><End>'''</End>
            </Span>
            <Span color=""String"" multiline=""true"">
                <Begin>&quot;&quot;&quot;</Begin><End>&quot;&quot;&quot;</End>
            </Span>
            <Span color=""String"">
                <Begin>&quot;</Begin><End>&quot;</End>
                <RuleSet><Span begin=""\\"" end=""."" /></RuleSet>
            </Span>
            <Span color=""String"">
                <Begin>'</Begin><End>'</End>
                <RuleSet><Span begin=""\\"" end=""."" /></RuleSet>
            </Span>
            <Keywords color=""Keyword"">
                <Word>class</Word><Word>def</Word><Word>global</Word><Word>nonlocal</Word>
                <Word>lambda</Word><Word>assert</Word><Word>del</Word><Word>pass</Word>
            </Keywords>
            <Keywords color=""ControlKeyword"">
                <Word>and</Word><Word>as</Word><Word>break</Word><Word>continue</Word>
                <Word>elif</Word><Word>else</Word><Word>except</Word><Word>False</Word>
                <Word>finally</Word><Word>for</Word><Word>from</Word><Word>if</Word>
                <Word>import</Word><Word>in</Word><Word>is</Word><Word>not</Word>
                <Word>or</Word><Word>raise</Word><Word>return</Word><Word>True</Word>
                <Word>try</Word><Word>while</Word><Word>with</Word><Word>yield</Word><Word>None</Word>
            </Keywords>
            <Keywords color=""BuiltIn"">
                <Word>print</Word><Word>len</Word><Word>range</Word><Word>int</Word>
                <Word>float</Word><Word>str</Word><Word>list</Word><Word>dict</Word>
                <Word>set</Word><Word>tuple</Word><Word>type</Word><Word>super</Word>
            </Keywords>
            <Rule color=""Number"">
                \b0[xX][0-9a-fA-F]+|(\b\d+(\.[0-9]+)?|\.[0-9]+)([eE][+-]?[0-9]+)?
            </Rule>
        </RuleSet>
    </SyntaxDefinition>";

            using (var reader = new System.Xml.XmlTextReader(new System.IO.StringReader(xshd)))
            {
                _vsCodeLightHighlighting = ICSharpCode.AvalonEdit.Highlighting.Xshd.HighlightingLoader.Load(reader, ICSharpCode.AvalonEdit.Highlighting.HighlightingManager.Instance);
            }
            return _vsCodeLightHighlighting;
        }

        private void AddTab(DocumentModel doc)
        {
            var editor = new TextEditor
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 13,
                ShowLineNumbers = true,
                SyntaxHighlighting = GetVSCodeLightHighlighting(),
                Background = System.Windows.Media.Brushes.White,
                Foreground = System.Windows.Media.Brushes.Black,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
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
                _viewModel.ConsoleLines.Add($"加载目录树出错: {ex.Message}");
            }
        }

        private TreeViewItem CreateDirectoryNode(string path)
        {
            var node = new TreeViewItem { Tag = path };

            var folderStack = new StackPanel { Orientation = Orientation.Horizontal };
            folderStack.Children.Add(new PackIcon
            {
                Kind = PackIconKind.FolderOutline,
                Width = 16, Height = 16,
                Margin = new System.Windows.Thickness(0, 0, 6, 0),
                Foreground = Brushes.Goldenrod,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            folderStack.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileName(path),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            node.Header = folderStack;

            if (string.IsNullOrEmpty(System.IO.Path.GetFileName(path)))
            {
                ((TextBlock)folderStack.Children[1]).Text = path;
            }

            try
            {
                foreach (string directory in System.IO.Directory.GetDirectories(path))
                    node.Items.Add(CreateDirectoryNode(directory));

                foreach (string file in System.IO.Directory.GetFiles(path))
                {
                    var fileNode = new TreeViewItem { Tag = file };

                    var fileStack = new StackPanel { Orientation = Orientation.Horizontal };

                    bool isPython = file.EndsWith(".py", System.StringComparison.OrdinalIgnoreCase);
                    PackIconKind iconKind = isPython ? PackIconKind.LanguagePython : PackIconKind.FileDocumentOutline;
                    Brush iconColor = isPython ? Brushes.CornflowerBlue : Brushes.Gray;

                    fileStack.Children.Add(new PackIcon
                    {
                        Kind = iconKind,
                        Width = 16, Height = 16,
                        Margin = new System.Windows.Thickness(0, 0, 6, 0),
                        Foreground = iconColor,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    });
                    fileStack.Children.Add(new TextBlock
                    {
                        Text = System.IO.Path.GetFileName(file),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    });

                    fileNode.Header = fileStack;
                    node.Items.Add(fileNode);
                }
            }
            catch (System.UnauthorizedAccessException) { }

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
