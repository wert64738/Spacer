﻿using System;
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
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".ico" };
        private static readonly string[] MovieExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm" };
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".aac", ".ogg", ".flac", ".m4a" };
        private static readonly string[] TextExtensions = { ".txt", ".md", ".log", ".csv", ".rtf" };
        private static readonly string[] OfficeExtensions = { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" };
        private static readonly string[] PDFExtensions = { ".pdf" };
        private static readonly string[] CompressedExtensions = { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".iso" };
        private static readonly string[] CodeExtensions = { ".cs", ".cpp", ".c", ".java", ".py", ".js", ".html", ".css", ".php", ".rb", ".go" };
        private static readonly string[] BinaryExtensions = { ".exe", ".dll", ".bin", ".dat", ".sys" };
        private static readonly string[] DatabaseExtensions = { ".db", ".sql", ".mdb", ".accdb", ".sqlite" };
        private static readonly string[] VectorExtensions = { ".svg", ".eps", ".ai" };

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

                    _rootFolder = await Task.Run(() => BuildFolderTree(selectedPath));

                    MainCanvas.Children.Clear();
                    RenderFolderMap(_rootFolder, MainCanvas, 0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight);
                }
            }
            finally
            {
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
                    if (fileInfo.Length >= 100)
                    {
                        node.Files.Add((file, fileInfo.Length));
                    }
                }

                foreach (var folder in subFolders)
                {
                    var subNode = BuildFolderTree(folder);
                    if (subNode != null)
                    {
                        subNode.Parent = node;
                        if (subNode.TotalSize > 0)
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
            const double gap = 2;           // Reduced gap between boxes and folder border.
            const double rollupThreshold = 3;
            const double textMinWidth = 40;
            const double textMinHeight = 20;

            if (node == null || width <= 0 || height <= 0)
                return;

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

            items.Sort((a, b) => b.Size.CompareTo(a.Size));
            double totalSize = items.Sum(item => item.Size);

            Rect innerArea = new Rect(x + gap, y + gap, Math.Max(0, width - 2 * gap), Math.Max(0, height - 2 * gap));
            DivideDisplayArea(items, 0, items.Count, innerArea, totalSize, gap);

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

            foreach (var item in items)
            {
                if (item.Rect.Width <= 0 || item.Rect.Height <= 0)
                    continue;

                var rect = new Rectangle
                {
                    Width = item.Rect.Width,
                    Height = item.Rect.Height,
                    Stroke = Brushes.DarkGray,
                    ToolTip = item.IsRollup ? $"Rolled up {item.Size} bytes" : item.Path
                };

                if (item.IsRollup)
                    rect.Fill = Brushes.Gray;
                else if (item.IsFolder)
                    rect.Fill = Brushes.LightGreen;
                else
                    rect.Fill = GetFileTypeColor(item.Path);

                canvas.Children.Add(rect);
                Canvas.SetLeft(rect, item.Rect.X);
                Canvas.SetTop(rect, item.Rect.Y);

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

                    // Double-click to open file.
                    rect.MouseLeftButtonDown += (s, e) =>
                    {
                        if (e.ClickCount == 2)
                        {
                            try
                            {
                                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("Could not open file: " + ex.Message);
                            }
                            e.Handled = true;
                        }
                    };
                }

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
                        FontSize = 7,
                        FontWeight = FontWeights.Bold,
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

                    const double folderContentPadding = 6;
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

        // Updated GetFileTypeColor: uses a base color per category and then varies it slightly based on the file extension.
        private Brush GetFileTypeColor(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            Color baseColor;
            if (ImageExtensions.Contains(ext))
                baseColor = Colors.PeachPuff;      // Base for images
            else if (MovieExtensions.Contains(ext))
                baseColor = Colors.LemonChiffon;     // Base for movies
            else if (AudioExtensions.Contains(ext))
                baseColor = Colors.MediumOrchid;     // Base for audio
            else if (TextExtensions.Contains(ext))
                baseColor = Colors.Thistle;          // Base for text files
            else if (OfficeExtensions.Contains(ext))
                baseColor = Colors.PaleGreen;        // Base for office docs
            else if (PDFExtensions.Contains(ext))
                baseColor = Colors.Khaki;            // Base for PDFs
            else if (CompressedExtensions.Contains(ext))
                baseColor = Colors.Gold;             // Base for compressed files
            else if (CodeExtensions.Contains(ext))
                baseColor = Colors.LightSlateGray;   // Base for code
            else if (BinaryExtensions.Contains(ext))
                baseColor = Colors.PowderBlue;       // Base for binaries
            else if (DatabaseExtensions.Contains(ext))
                baseColor = Colors.DarkSeaGreen;     // Base for databases
            else if (VectorExtensions.Contains(ext))
                baseColor = Colors.LightPink;        // Base for vector graphics
            else
                baseColor = Colors.LightBlue;        // Default

            return new SolidColorBrush(VaryColorForExtension(ext, baseColor));
        }

        // Vary the base color's lightness slightly based on the extension string.
        private Color VaryColorForExtension(string ext, Color baseColor)
        {
            int hash = ext.GetHashCode();
            // Compute a variation offset in the range [-0.05, +0.05]
            double offset = ((hash % 11) - 5) / 100.0;
            double h, s, l;
            ColorToHSL(baseColor, out h, out s, out l);
            l = Math.Max(0, Math.Min(1, l + offset));
            return ColorFromHSL(h, s, l);
        }

        // Helper: Convert a Color to HSL.
        private void ColorToHSL(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (max == min)
            {
                h = s = 0;
            }
            else
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r)
                    h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g)
                    h = (b - r) / d + 2;
                else
                    h = (r - g) / d + 4;
                h /= 6.0;
            }
        }

        private double HueToRGB(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        private Color ColorFromHSL(double h, double s, double l)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRGB(p, q, h + 1.0 / 3.0);
                g = HueToRGB(p, q, h);
                b = HueToRGB(p, q, h - 1.0 / 3.0);
            }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
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
