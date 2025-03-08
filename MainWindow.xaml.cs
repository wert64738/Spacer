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
        private void RenderFolderMap(FolderNode node, Canvas canvas, double x, double y, double width, double height, bool verticalSplit = true)
        {
            if (node == null || width <= 0 || height <= 0)
                return;

            // Use a constant gap for both container spacing and folder content buffer.
            const double containerGap = 3;

            var items = node.SubFolders
                            .Select(f => new { f.Path, Size = f.TotalSize, IsFolder = true, Folder = f })
                            .Concat(node.Files.Select(f => new { f.Path, Size = f.Size, IsFolder = false, Folder = (FolderNode)null }))
                            .OrderByDescending(item => item.Size)
                            .ToList();

            if (items.Count == 0)
                return;

            double totalChildSize = items.Sum(item => item.Size);

            // Total gap space between items: (number of gaps) = (items.Count - 1) * containerGap.
            double totalGap = (items.Count - 1) * containerGap;
            double availableMain = verticalSplit ? width - totalGap : height - totalGap;
            if (availableMain < 0)
                availableMain = 0;

            double currentMainOffset = 0;
            double rollUpAccum = 0;
            double rollUpChildSize = 0;

            foreach (var item in items)
            {
                double allocatedMain = (item.Size / totalChildSize) * availableMain;

                // "Roll up" items that would get less than 3 pixels.
                if (allocatedMain < 3)
                {
                    rollUpAccum += allocatedMain;
                    rollUpChildSize += item.Size;
                }
                else
                {
                    // Flush any accumulated roll-up items.
                    if (rollUpAccum > 0)
                    {
                        DrawRolledUpRect(canvas, x, y, verticalSplit, ref currentMainOffset, rollUpAccum, width, height, rollUpChildSize, containerGap);
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

                    // For folders, inset the recursive layout by containerGap on all sides.
                    if (item.IsFolder)
                    {
                        RenderFolderMap(item.Folder, canvas,
                                        itemX + containerGap, itemY + containerGap,
                                        Math.Max(0, itemWidth - 2 * containerGap), Math.Max(0, itemHeight - 2 * containerGap),
                                        !verticalSplit);
                    }

                    currentMainOffset += allocatedMain;
                    if (item != items.Last())
                        currentMainOffset += containerGap;
                }
            }

            // Flush any remaining rolled-up items.
            if (rollUpAccum > 0)
            {
                DrawRolledUpRect(canvas, x, y, verticalSplit, ref currentMainOffset, rollUpAccum, width, height, rollUpChildSize, containerGap);
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
