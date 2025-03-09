using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Spacer
{
    public partial class MainWindow : Window
    {
        // Define file type extension lists.
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff" };
        private static readonly string[] MovieExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv" };
        private static readonly string[] TextExtensions = { ".txt", ".md", ".log", ".csv", ".xml", ".json" };
        private static readonly string[] CompressedExtensions = { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".iso" };
        private static readonly string[] BinaryExtensions = { ".exe", ".dll", ".bin", ".dat", ".sys" };

        public MainWindow()
        {
            InitializeComponent();
        }

        private FolderNode _rootFolder;

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_rootFolder != null)
            {
                MainCanvas.Children.Clear();
                RenderFolderMap(_rootFolder, MainCanvas, 0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight);
            }
        }

        private async void ScanFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Show processing indicator and dim main display.
            ProcessingIndicator.Visibility = Visibility.Visible;
            ProcessingIndicator.Text = "Scanning directories...";
            MainCanvas.Opacity = 0.5;

            // Create a flashing animation.
            DoubleAnimation flashAnimation = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(0.5)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            ProcessingIndicator.BeginAnimation(UIElement.OpacityProperty, flashAnimation);

            try
            {
                var folderDialog = new Microsoft.Win32.OpenFileDialog
                {
                    CheckFileExists = false,
                    CheckPathExists = true,
                    FileName = "Select Folder",
                    Filter = "Folders|*."
                };

                bool? result = folderDialog.ShowDialog();
                if (result == true)
                {
                    string selectedPath = System.IO.Path.GetDirectoryName(folderDialog.FileName);
                    RootFolderTextBox.Text = selectedPath;

                    // Run the folder scan in a background task.
                    _rootFolder = await Task.Run(() => BuildFolderTree(selectedPath));

                    MainCanvas.Children.Clear();
                    RenderFolderMap(_rootFolder, MainCanvas, 0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight);
                }
            }
            finally
            {
                // Stop flashing and restore full opacity.
                ProcessingIndicator.BeginAnimation(UIElement.OpacityProperty, null);
                ProcessingIndicator.Visibility = Visibility.Collapsed;
                MainCanvas.Opacity = 1.0;
            }
        }

        private FolderNode BuildFolderTree(string path)
        {
            var node = new FolderNode { Path = path };
            try
            {
                var files = Directory.GetFiles(path);
                var subFolders = Directory.GetDirectories(path);

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.Length >= 100) // Ignore files smaller than 100 bytes
                    {
                        node.Files.Add((file, fileInfo.Length));
                    }
                }

                foreach (var folder in subFolders)
                {
                    var subNode = BuildFolderTree(folder);
                    if (subNode != null)
                    {
                        subNode.Parent = node; // assign parent for zoom-out
                        if (subNode.TotalSize > 0) // Ignore empty folders
                        {
                            node.SubFolders.Add(subNode);
                        }
                    }
                }
            }
            catch { }

            return node;
        }

        private void RenderFolderMap(FolderNode node, Canvas canvas, double x, double y, double width, double height)
        {
            const double gap = 3;           // Gap between boxes and folder border
            const double rollupThreshold = 3; // Minimum size (in pixels) for a box before rollup
            const double textMinWidth = 40;   // Minimum width to show text
            const double textMinHeight = 20;  // Minimum height to show text

            if (node == null || width <= 0 || height <= 0)
                return;

            // Build list of child items.
            List<ChildItem> items = new List<ChildItem>();
            foreach (var folder in node.SubFolders)
            {
                items.Add(new ChildItem { Path = folder.Path, Size = folder.TotalSize, IsFolder = true, Folder = folder });
            }
            foreach (var file in node.Files)
            {
                items.Add(new ChildItem { Path = file.Path, Size = file.Size, IsFolder = false, Folder = null });
            }
            if (items.Count == 0)
                return;

            // Sort items descending by size.
            items.Sort((a, b) => b.Size.CompareTo(a.Size));
            double totalSize = items.Sum(item => item.Size);

            // Reserve a 3-pixel border inside the folder.
            Rect innerArea = new Rect(x + gap, y + gap, Math.Max(0, width - 2 * gap), Math.Max(0, height - 2 * gap));

            // First layout pass.
            DivideDisplayArea(items, 0, items.Count, innerArea, totalSize, gap);

            // Identify tiny items.
            var tinyItems = items.Where(item => item.Rect.Width < rollupThreshold || item.Rect.Height < rollupThreshold).ToList();
            if (tinyItems.Any())
            {
                double rollupSize = tinyItems.Sum(item => item.Size);
                items.RemoveAll(item => item.Rect.Width < rollupThreshold || item.Rect.Height < rollupThreshold);
                items.Add(new ChildItem { Path = "Rollup", Size = rollupSize, IsFolder = false, IsRollup = true });
                items.Sort((a, b) => b.Size.CompareTo(a.Size));
                totalSize = items.Sum(item => item.Size);
                DivideDisplayArea(items, 0, items.Count, innerArea, totalSize, gap);
            }

            // Render each item.
            foreach (var item in items)
            {
                if (item.Rect.Width <= 0 || item.Rect.Height <= 0)
                    continue;

                var rect = new Rectangle
                {
                    Width = item.Rect.Width,
                    Height = item.Rect.Height,
                    Stroke = Brushes.Black,
                    ToolTip = item.IsRollup ? $"Rolled up {item.Size} bytes" : item.Path
                };

                if (item.IsRollup)
                    rect.Fill = Brushes.Gray;
                else if (item.IsFolder)
                    rect.Fill = Brushes.LightGreen;
                else // File items: choose color based on extension.
                    rect.Fill = GetFileTypeColor(item.Path);

                canvas.Children.Add(rect);
                Canvas.SetLeft(rect, item.Rect.X);
                Canvas.SetTop(rect, item.Rect.Y);

                // For file items, attach a right-click context menu.
                if (!item.IsFolder && !item.IsRollup)
                {
                    ContextMenu cm = new ContextMenu();

                    MenuItem miOpen = new MenuItem { Header = "Open" };
                    miOpen.Click += (s, e) =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Could not open file: " + ex.Message);
                        }
                    };

                    MenuItem miDelete = new MenuItem { Header = "Delete" };
                    miDelete.Click += async (s, e) =>
                    {
                        if (MessageBox.Show($"Are you sure you want to delete '{System.IO.Path.GetFileName(item.Path)}'?",
                            "Confirm Delete", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.IO.File.Delete(item.Path);
                                // After deletion, rescan the current folder.
                                _rootFolder = await Task.Run(() => BuildFolderTree(_rootFolder.Path));
                                MainCanvas.Children.Clear();
                                RenderFolderMap(_rootFolder, MainCanvas, 0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Error deleting file: " + ex.Message);
                            }
                        }
                    };

                    cm.Items.Add(miOpen);
                    cm.Items.Add(miDelete);
                    rect.ContextMenu = cm;
                }

                // For file items that are big enough, add centered text.
                if (!item.IsFolder && !item.IsRollup && item.Rect.Width >= textMinWidth && item.Rect.Height >= textMinHeight)
                {
                    string fileName = System.IO.Path.GetFileName(item.Path);

                    Grid grid = new Grid
                    {
                        Width = item.Rect.Width,
                        Height = item.Rect.Height
                    };
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    TextBlock filenameText = new TextBlock
                    {
                        Text = fileName,
                        FontSize = 6,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Width = item.Rect.Width
                    };
                    Grid.SetRow(filenameText, 0);

                    TextBlock filesizeText = new TextBlock
                    {
                        Text = FormatSize(item.Size),
                        FontSize = 6,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetRow(filesizeText, 1);

                    grid.Children.Add(filenameText);
                    grid.Children.Add(filesizeText);

                    grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Size gridSize = grid.DesiredSize;
                    double gridX = item.Rect.X + (item.Rect.Width - gridSize.Width) / 2;
                    double gridY = item.Rect.Y + (item.Rect.Height - gridSize.Height) / 2;
                    canvas.Children.Add(grid);
                    Canvas.SetLeft(grid, gridX);
                    Canvas.SetTop(grid, gridY);
                }

                // For folders, add folder name at the very top (flush) and enable double-click zoom.
                if (item.IsFolder && !item.IsRollup)
                {
                    rect.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount == 2)
                        {
                            ZoomToFolder(item.Folder);
                            e.Handled = true;
                        }
                    };

                    string folderName = System.IO.Path.GetFileName(item.Path);
                    if (string.IsNullOrEmpty(folderName))
                        folderName = item.Path;

                    TextBlock folderText = new TextBlock
                    {
                        Text = folderName,
                        FontSize = 6,
                        Foreground = Brushes.Black,
                        Background = Brushes.Transparent,
                        TextAlignment = TextAlignment.Center,
                        Width = item.Rect.Width,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Top,
                        IsHitTestVisible = false
                    };
                    canvas.Children.Add(folderText);
                    Canvas.SetLeft(folderText, item.Rect.X);
                    Canvas.SetTop(folderText, item.Rect.Y);

                    const double folderContentPadding = 5;
                    RenderFolderMap(item.Folder, canvas,
                        item.Rect.X + gap, item.Rect.Y + gap + folderContentPadding,
                        Math.Max(0, item.Rect.Width - 2 * gap), Math.Max(0, item.Rect.Height - 2 * gap - folderContentPadding));
                }
            }
        }

        private void DivideDisplayArea(List<ChildItem> items, int start, int count, Rect area, double totalSize, double gap)
        {
            double safeWidth = Math.Max(0, area.Width);
            double safeHeight = Math.Max(0, area.Height);
            area = new Rect(area.X, area.Y, safeWidth, safeHeight);

            if (count <= 0)
                return;
            if (count == 1)
            {
                items[start].Rect = area;
                return;
            }

            int groupACount = 0;
            double sizeA = 0;
            int i = start;
            sizeA += items[i].Size;
            groupACount++;
            i++;
            while (i < start + count && (sizeA + items[i].Size) * 2 < totalSize && items[i].Size > 0)
            {
                sizeA += items[i].Size;
                groupACount++;
                i++;
            }
            int groupBCount = count - groupACount;
            double sizeB = totalSize - sizeA;

            bool horizontalSplit = safeWidth >= safeHeight;
            if (horizontalSplit)
            {
                double effectiveWidth = Math.Max(0, safeWidth - gap);
                double mid = (totalSize > 0) ? (sizeA / totalSize) * effectiveWidth : effectiveWidth / 2;
                Rect areaA = new Rect(area.X, area.Y, Math.Max(0, mid), safeHeight);
                Rect areaB = new Rect(area.X + mid + gap, area.Y, Math.Max(0, safeWidth - mid - gap), safeHeight);

                DivideDisplayArea(items, start, groupACount, areaA, sizeA, gap);
                DivideDisplayArea(items, start + groupACount, groupBCount, areaB, sizeB, gap);
            }
            else
            {
                double effectiveHeight = Math.Max(0, safeHeight - gap);
                double mid = (totalSize > 0) ? (sizeA / totalSize) * effectiveHeight : effectiveHeight / 2;
                Rect areaA = new Rect(area.X, area.Y, safeWidth, Math.Max(0, mid));
                Rect areaB = new Rect(area.X, area.Y + mid + gap, safeWidth, Math.Max(0, safeHeight - mid - gap));

                DivideDisplayArea(items, start, groupACount, areaA, sizeA, gap);
                DivideDisplayArea(items, start + groupACount, groupBCount, areaB, sizeB, gap);
            }
        }

        private string FormatSize(double size)
        {
            if (size < 1024)
                return $"{size:F0} B";
            double kb = size / 1024;
            if (kb < 1024)
                return $"{kb:F1} KB";
            double mb = kb / 1024;
            if (mb < 1024)
                return $"{mb:F1} MB";
            double gb = mb / 1024;
            return $"{gb:F1} GB";
        }

        private Brush GetFileTypeColor(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

            if (ImageExtensions.Contains(ext))
                return new SolidColorBrush(Colors.PeachPuff);      // Pastel orange for images
            else if (MovieExtensions.Contains(ext))
                return new SolidColorBrush(Colors.LemonChiffon);     // Pastel yellow for movies
            else if (TextExtensions.Contains(ext))
                return new SolidColorBrush(Colors.Thistle);          // Pastel purple for text files
            else if (CompressedExtensions.Contains(ext))
                return new SolidColorBrush(Colors.Yellow);           // Yellow for compressed files
            else if (BinaryExtensions.Contains(ext))
                return new SolidColorBrush(Colors.PowderBlue);       // Pastel blue for binary files
            else
                return Brushes.LightBlue;                          // Default color
        }

        private async void ZoomToFolder(FolderNode folder)
        {
            ProcessingIndicator.Visibility = Visibility.Visible;
            ProcessingIndicator.Text = "Processing...";
            MainCanvas.Opacity = 0.5;
            await Task.Yield();

            try
            {
                _rootFolder = folder;
                RootFolderTextBox.Text = folder.Path;
                MainCanvas.Children.Clear();
                double width = MainCanvas.ActualWidth;
                double height = MainCanvas.ActualHeight;
                RenderFolderMap(folder, MainCanvas, 0, 0, width, height);
            }
            finally
            {
                ProcessingIndicator.Visibility = Visibility.Collapsed;
                MainCanvas.Opacity = 1.0;
            }
        }

        private async void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            ProcessingIndicator.Visibility = Visibility.Visible;
            ProcessingIndicator.Text = "Processing...";
            MainCanvas.Opacity = 0.5;
            await Task.Yield();

            try
            {
                if (_rootFolder != null && _rootFolder.Parent != null)
                {
                    ZoomToFolder(_rootFolder.Parent);
                }
                else
                {
                    DirectoryInfo parentDir = Directory.GetParent(_rootFolder.Path);
                    if (parentDir != null)
                    {
                        FolderNode newRoot = BuildFolderTree(parentDir.FullName);
                        ZoomToFolder(newRoot);
                    }
                    else
                    {
                        MessageBox.Show("Already at the drive root.");
                    }
                }
            }
            finally
            {
                ProcessingIndicator.Visibility = Visibility.Collapsed;
                MainCanvas.Opacity = 1.0;
            }
        }

        class FolderNode
        {
            public string Path { get; set; }
            public FolderNode Parent { get; set; }
            public List<(string Path, long Size)> Files { get; set; } = new List<(string, long)>();
            public List<FolderNode> SubFolders { get; set; } = new List<FolderNode>();
            public long TotalSize => Files.Sum(f => f.Size) + SubFolders.Sum(f => f.TotalSize);
        }

        private class ChildItem
        {
            public string Path;
            public double Size;
            public bool IsFolder;
            public FolderNode Folder;
            public Rect Rect;
            public bool IsRollup;
        }
    }
}
