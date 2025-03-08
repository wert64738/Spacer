using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Spacer
{
    public partial class MainWindow : Window
    {
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

        private void ScanFolderButton_Click(object sender, RoutedEventArgs e)
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
                _rootFolder = BuildFolderTree(selectedPath);  // Save the folder tree
                MainCanvas.Children.Clear();
                RenderFolderMap(_rootFolder, MainCanvas, 0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight);
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
                    if (subNode.TotalSize > 0) // Ignore empty folders
                    {
                        node.SubFolders.Add(subNode);
                    }
                }
            }
            catch { }

            return node;
        }

        private void RenderFolderMap(FolderNode node, Canvas canvas, double x, double y, double width, double height)
        {
            const double gap = 3;                // Gap between boxes and folder border
            const double rollupThreshold = 3;      // Minimum size (in pixels) for a box
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

            // Identify tiny items (allocated less than rollupThreshold in either dimension).
            var tinyItems = items.Where(item => item.Rect.Width < rollupThreshold || item.Rect.Height < rollupThreshold).ToList();
            if (tinyItems.Any())
            {
                // Aggregate their sizes.
                double rollupSize = tinyItems.Sum(item => item.Size);
                // Remove them from the list.
                items.RemoveAll(item => item.Rect.Width < rollupThreshold || item.Rect.Height < rollupThreshold);
                // Add an aggregated rollup item.
                items.Add(new ChildItem { Path = "Rollup", Size = rollupSize, IsFolder = false, IsRollup = true });
                // Re-sort and recalc total.
                items.Sort((a, b) => b.Size.CompareTo(a.Size));
                totalSize = items.Sum(item => item.Size);
                // Re-run the layout with the updated items list.
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
                else
                    rect.Fill = item.IsFolder ? Brushes.LightGreen : Brushes.LightBlue;

                canvas.Children.Add(rect);
                Canvas.SetLeft(rect, item.Rect.X);
                Canvas.SetTop(rect, item.Rect.Y);

                // Recurse into folders (but not for rollup items).
                if (item.IsFolder && !item.IsRollup)
                {
                    RenderFolderMap(item.Folder, canvas,
                        item.Rect.X + gap, item.Rect.Y + gap,
                        Math.Max(0, item.Rect.Width - 2 * gap), Math.Max(0, item.Rect.Height - 2 * gap));
                }
            }
        }

        private void DivideDisplayArea(List<ChildItem> items, int start, int count, Rect area, double totalSize, double gap)
        {
            // Ensure we work with non-negative dimensions.
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

            // Partition items into two groups (A and B) to balance sizes.
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

            // Choose orientation based on aspect ratio.
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

        private class ChildItem
        {
            public string Path;
            public double Size;
            public bool IsFolder;
            public FolderNode Folder;
            public Rect Rect;
            public bool IsRollup; // True if this item represents aggregated tiny items.
        }


        class FolderNode 
        {
            public string Path { get; set; }
            public List<(string Path, long Size)> Files { get; set; } = new List<(string, long)>();
            public List<FolderNode> SubFolders { get; set; } = new List<FolderNode>();
            public long TotalSize => Files.Sum(f => f.Size) + SubFolders.Sum(f => f.TotalSize);
        }
    }
}
