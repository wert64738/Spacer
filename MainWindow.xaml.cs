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

        private void RenderFolderMap(FolderNode node, Canvas canvas, double x, double y, double width, double height, bool verticalSplit = true)
        {
            if (node == null || width <= 0 || height <= 0)
                return;

            var items = node.SubFolders
                            .Select(f => new { f.Path, Size = f.TotalSize, IsFolder = true, Folder = f })
                            .Concat(node.Files.Select(f => new { f.Path, Size = f.Size, IsFolder = false, Folder = (FolderNode)null }))
                            .OrderByDescending(item => item.Size)
                            .ToList();
            if (items.Count == 0)
                return;

            double totalChildSize = items.Sum(item => item.Size);
            double folderPadding = 4;
            double filePadding = 1;

            // Compute total gap space between items.
            double totalGap = 0;
            for (int i = 0; i < items.Count - 1; i++)
                totalGap += items[i].IsFolder ? folderPadding : filePadding;

            // Determine available space in the primary direction.
            double availableMain = verticalSplit ? width - totalGap : height - totalGap;
            if (availableMain < 0)
                availableMain = 0;

            double currentMainOffset = 0;
            double rollUpAccum = 0;      // Accumulated allocation for tiny items.
            double rollUpChildSize = 0;  // Accumulated size for tooltip info.

            foreach (var item in items)
            {
                double allocatedMain = (item.Size / totalChildSize) * availableMain;
                // If the allocated space is less than 3 pixels, accumulate for rollup.
                if (allocatedMain < 3)
                {
                    rollUpAccum += allocatedMain;
                    rollUpChildSize += item.Size;
                }
                else
                {
                    // Flush any pending rolled-up items before rendering a normal item.
                    if (rollUpAccum > 0)
                    {
                        DrawRolledUpRect(canvas, x, y, verticalSplit, ref currentMainOffset, rollUpAccum, width, height, rollUpChildSize, folderPadding);
                        rollUpAccum = 0;
                        rollUpChildSize = 0;
                    }

                    double itemX, itemY, itemWidth, itemHeight;
                    if (verticalSplit)
                    {
                        itemX = x + currentMainOffset;
                        itemY = y;
                        itemWidth = allocatedMain;
                        itemHeight = height;
                    }
                    else
                    {
                        itemX = x;
                        itemY = y + currentMainOffset;
                        itemWidth = width;
                        itemHeight = allocatedMain;
                    }

                    var rect = new Rectangle
                    {
                        Width = itemWidth,
                        Height = itemHeight,
                        Fill = item.IsFolder ? Brushes.LightGreen : Brushes.LightBlue,
                        Stroke = Brushes.Black,
                        ToolTip = item.Path
                    };
                    canvas.Children.Add(rect);
                    Canvas.SetLeft(rect, itemX);
                    Canvas.SetTop(rect, itemY);

                    if (item.IsFolder)
                    {
                        RenderFolderMap(item.Folder, canvas, itemX, itemY, itemWidth, itemHeight, !verticalSplit);
                    }

                    currentMainOffset += allocatedMain;
                    if (item != items.Last())
                    {
                        double gap = item.IsFolder ? folderPadding : filePadding;
                        currentMainOffset += gap;
                    }
                }
            }

            // Flush any remaining rolled-up items.
            if (rollUpAccum > 0)
            {
                DrawRolledUpRect(canvas, x, y, verticalSplit, ref currentMainOffset, rollUpAccum, width, height, rollUpChildSize, folderPadding);
            }
        }

        private void DrawRolledUpRect(Canvas canvas, double x, double y, bool verticalSplit, ref double currentMainOffset,
                                      double allocatedMain, double totalWidth, double totalHeight, double totalRolledUpSize, double gap)
        {
            double itemX, itemY, itemWidth, itemHeight;
            if (verticalSplit)
            {
                itemX = x + currentMainOffset;
                itemY = y;
                itemWidth = allocatedMain;
                itemHeight = totalHeight;
            }
            else
            {
                itemX = x;
                itemY = y + currentMainOffset;
                itemWidth = totalWidth;
                itemHeight = allocatedMain;
            }
            var rollUpRect = new Rectangle
            {
                Width = itemWidth,
                Height = itemHeight,
                Fill = Brushes.Gray,
                Stroke = Brushes.Black,
                ToolTip = $"Rolled up {totalRolledUpSize} bytes"
            };
            canvas.Children.Add(rollUpRect);
            Canvas.SetLeft(rollUpRect, itemX);
            Canvas.SetTop(rollUpRect, itemY);
            currentMainOffset += allocatedMain + gap;
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
