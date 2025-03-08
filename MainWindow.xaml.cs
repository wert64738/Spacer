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
                var rootFolder = BuildFolderTree(selectedPath);
                MainCanvas.Children.Clear();
                RenderFolderMap(rootFolder, MainCanvas, 0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight);
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
                    if (fileInfo.Length >= 1024) // Ignore files smaller than 1KB
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
            double totalSize = node.TotalSize;
            if (totalSize == 0 || width <= 0 || height <= 0) return;

            double folderPadding = 4;
            double filePadding = 1;
            double offsetX = x + folderPadding;
            double offsetY = y + folderPadding;
            double adjustedWidth = Math.Max(0, width - 2 * folderPadding);
            double adjustedHeight = Math.Max(0, height - 2 * folderPadding);

            var allItems = node.SubFolders.Select(f => (f.Path, f.TotalSize, true)).Concat(node.Files.Select(f => (f.Path, f.Size, false))).OrderByDescending(f => f.Item2).ToList();

            foreach (var (path, size, isFolder) in allItems)
            {
                double proportion = size / totalSize;
                double rectWidth = Math.Max(0, adjustedWidth * Math.Sqrt(proportion));
                double rectHeight = Math.Max(0, adjustedHeight * Math.Sqrt(proportion));

                rectWidth = Math.Max(0, Math.Min(rectWidth, adjustedWidth - (offsetX - x)));
                rectHeight = Math.Max(0, Math.Min(rectHeight, adjustedHeight - (offsetY - y)));

                var rect = new Rectangle
                {
                    Width = rectWidth,
                    Height = rectHeight,
                    Fill = isFolder ? Brushes.LightGreen : Brushes.LightBlue,
                    Stroke = Brushes.Black,
                    ToolTip = path
                };
                canvas.Children.Add(rect);
                Canvas.SetLeft(rect, offsetX);
                Canvas.SetTop(rect, offsetY);

                if (isFolder)
                {
                    var subFolder = node.SubFolders.First(f => f.Path == path);
                    RenderFolderMap(subFolder, canvas, offsetX, offsetY, rectWidth, rectHeight);
                    offsetX += rectWidth + folderPadding;
                }
                else
                {
                    offsetX += rectWidth + filePadding;
                }

                if (offsetX > x + adjustedWidth)
                {
                    offsetX = x + folderPadding;
                    offsetY += rectHeight + (isFolder ? folderPadding : filePadding);
                }
            }
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
