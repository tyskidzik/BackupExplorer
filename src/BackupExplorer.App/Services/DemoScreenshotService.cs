using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BackupExplorer.App.Models;
using BackupExplorer.App.Services;
using BackupExplorer.App.ViewModels;

namespace BackupExplorer.App.Services;

public static class DemoScreenshotService
{
    public static void GenerateMockScreenshots(string[] outputDirectories)
    {
        foreach (var dir in outputDirectories)
        {
            try { Directory.CreateDirectory(dir); } catch { }
        }

        var vm = CreateMockViewModel();

        var window = new MainWindow(vm)
        {
            Width = 1240,
            Height = 780,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        window.Show();
        PumpDispatcher();

        // 1. Capture Explorer View
        CaptureWindow(window, "explorer_view.png", outputDirectories);

        // 2. Setup Mock Progress Modal State
        vm.Execution.IsExecuting = true;
        vm.Execution.StatusMessage = "Processing 18 of 24: LevelData.bin";
        vm.Execution.CurrentFileName = "Drive_D/Projects/GameEngine/LevelData.bin";
        vm.Execution.OverallProgress = 72.0;
        vm.Execution.ProcessedBytesFormatted = "125.7 MB of 174.6 MB";
        vm.Execution.SpeedFormatted = "64.2 MB/s";
        vm.Execution.RemainingFormatted = "00:00:01";
        vm.IsProgressModalOpen = true;

        PumpDispatcher();

        // 3. Capture Progress Modal View
        CaptureWindow(window, "progress_modal.png", outputDirectories);

        window.Close();
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f =>
        {
            ((DispatcherFrame)f).Continue = false;
            return null;
        }), frame);
        Dispatcher.PushFrame(frame);
        Thread.Sleep(300);
    }

    private static void CaptureWindow(Window window, string filename, string[] outputDirectories)
    {
        window.UpdateLayout();
        int width = (int)Math.Round(window.ActualWidth);
        int height = (int)Math.Round(window.ActualHeight);
        if (width <= 0) width = 1240;
        if (height <= 0) height = 780;

        // Render onto solid dark surface to capture Mica/Fluent dark mode contrast perfectly
        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F)), null, new Rect(0, 0, width, height));
            var visualBrush = new VisualBrush(window) { Stretch = Stretch.None };
            dc.DrawRectangle(visualBrush, null, new Rect(0, 0, width, height));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(drawingVisual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        foreach (var dir in outputDirectories)
        {
            try
            {
                string targetPath = Path.Combine(dir, filename);
                using var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write);
                encoder.Save(stream);
            }
            catch { }
        }
    }

    private static MainViewModel CreateMockViewModel()
    {
        var explorer = new ExplorerViewModel(autoLoad: false);
        var vm = new MainViewModel(explorer: explorer);

        // 1. Anonymized Quick Access
        vm.Explorer.QuickAccessItems.Clear();
        vm.Explorer.QuickAccessItems.Add(new DriveOrFolderItem { Name = "Projects", FullPath = @"D:\Projects", IsDrive = false, Icon = ShellIconService.GetFolderIcon() });
        vm.Explorer.QuickAccessItems.Add(new DriveOrFolderItem { Name = "Documents", FullPath = @"C:\Documents", IsDrive = false, Icon = ShellIconService.GetFolderIcon() });
        vm.Explorer.QuickAccessItems.Add(new DriveOrFolderItem { Name = "Assets", FullPath = @"D:\Assets", IsDrive = false, Icon = ShellIconService.GetFolderIcon() });
        vm.Explorer.QuickAccessItems.Add(new DriveOrFolderItem { Name = "Archives", FullPath = @"E:\Archives", IsDrive = false, Icon = ShellIconService.GetFolderIcon() });

        // 2. Anonymized Drives (Clean labels, no personal names)
        vm.Explorer.DriveItems.Clear();
        vm.Explorer.DriveItems.Add(new DriveOrFolderItem
        {
            Name = "System (C:)",
            FullPath = @"C:\",
            IsDrive = true,
            TotalSize = 512L * 1024 * 1024 * 1024,
            FreeSpace = 128L * 1024 * 1024 * 1024,
            FreeSpaceFormatted = "128 GB free of 512 GB",
            UsedPercent = 75.0,
            Icon = ShellIconService.GetDriveIcon(@"C:\")
        });
        vm.Explorer.DriveItems.Add(new DriveOrFolderItem
        {
            Name = "Storage (D:)",
            FullPath = @"D:\",
            IsDrive = true,
            TotalSize = 2048L * 1024 * 1024 * 1024,
            FreeSpace = 1240L * 1024 * 1024 * 1024,
            FreeSpaceFormatted = "1.21 TB free of 2.0 TB",
            UsedPercent = 39.4,
            Icon = ShellIconService.GetDriveIcon(@"C:\")
        });
        vm.Explorer.DriveItems.Add(new DriveOrFolderItem
        {
            Name = "Backup Drive (E:)",
            FullPath = @"E:\",
            IsDrive = true,
            TotalSize = 4096L * 1024 * 1024 * 1024,
            FreeSpace = 3450L * 1024 * 1024 * 1024,
            FreeSpaceFormatted = "3.37 TB free of 4.0 TB",
            UsedPercent = 15.7,
            Icon = ShellIconService.GetDriveIcon(@"C:\")
        });

        // 3. Anonymized Breadcrumb & Path
        vm.Explorer.CurrentPath = @"D:\Projects\GameEngine";
        vm.Explorer.Breadcrumbs.Clear();
        vm.Explorer.Breadcrumbs.Add(new BreadcrumbItem { Name = "Storage (D:)", FullPath = @"D:\" });
        vm.Explorer.Breadcrumbs.Add(new BreadcrumbItem { Name = "Projects", FullPath = @"D:\Projects" });
        vm.Explorer.Breadcrumbs.Add(new BreadcrumbItem { Name = "GameEngine", FullPath = @"D:\Projects\GameEngine" });

        // 4. Anonymized Files & Folders
        var folderIcon = ShellIconService.GetFolderIcon();
        var mockItems = new List<ExplorerItem>
        {
            new ExplorerItem { Name = "assets", FullPath = @"D:\Projects\GameEngine\assets", IsDirectory = true, Extension = "Folder", SizeFormatted = "--", ModifiedDateFormatted = "2026-03-12 14:32", Icon = folderIcon },
            new ExplorerItem { Name = "build", FullPath = @"D:\Projects\GameEngine\build", IsDirectory = true, Extension = "Folder", SizeFormatted = "--", ModifiedDateFormatted = "2026-03-15 09:18", Icon = folderIcon },
            new ExplorerItem { Name = "docs", FullPath = @"D:\Projects\GameEngine\docs", IsDirectory = true, Extension = "Folder", SizeFormatted = "--", ModifiedDateFormatted = "2026-03-10 16:45", Icon = folderIcon },
            new ExplorerItem { Name = "src", FullPath = @"D:\Projects\GameEngine\src", IsDirectory = true, Extension = "Folder", SizeFormatted = "--", ModifiedDateFormatted = "2026-03-18 11:20", Icon = folderIcon, IsSelected = true },
            new ExplorerItem { Name = "App.xaml.cs", FullPath = @"D:\Projects\GameEngine\App.xaml.cs", IsDirectory = false, Extension = "CS", Size = 14500, SizeFormatted = "14.2 KB", ModifiedDateFormatted = "2026-03-18 11:05", Icon = ShellIconService.GetFileIcon("code.cs"), IsSelected = true },
            new ExplorerItem { Name = "CMakeLists.txt", FullPath = @"D:\Projects\GameEngine\CMakeLists.txt", IsDirectory = false, Extension = "TXT", Size = 3200, SizeFormatted = "3.1 KB", ModifiedDateFormatted = "2026-03-14 18:22", Icon = ShellIconService.GetFileIcon("doc.txt") },
            new ExplorerItem { Name = "Config.json", FullPath = @"D:\Projects\GameEngine\Config.json", IsDirectory = false, Extension = "JSON", Size = 2400, SizeFormatted = "2.3 KB", ModifiedDateFormatted = "2026-03-17 08:40", Icon = ShellIconService.GetFileIcon("data.json") },
            new ExplorerItem { Name = "DatabaseSchema.sql", FullPath = @"D:\Projects\GameEngine\DatabaseSchema.sql", IsDirectory = false, Extension = "SQL", Size = 8600, SizeFormatted = "8.4 KB", ModifiedDateFormatted = "2026-03-14 13:50", Icon = ShellIconService.GetFileIcon("db.sql") },
            new ExplorerItem { Name = "EngineCore.dll", FullPath = @"D:\Projects\GameEngine\EngineCore.dll", IsDirectory = false, Extension = "DLL", Size = 4520000, SizeFormatted = "4.31 MB", ModifiedDateFormatted = "2026-03-15 17:10", Icon = ShellIconService.GetFileIcon("lib.dll") },
            new ExplorerItem { Name = "LevelData.bin", FullPath = @"D:\Projects\GameEngine\LevelData.bin", IsDirectory = false, Extension = "BIN", Size = 28400000, SizeFormatted = "27.08 MB", ModifiedDateFormatted = "2026-03-16 19:12", Icon = ShellIconService.GetFileIcon("file.bin"), IsSelected = true },
            new ExplorerItem { Name = "Logo.png", FullPath = @"D:\Projects\GameEngine\Logo.png", IsDirectory = false, Extension = "PNG", Size = 524000, SizeFormatted = "511.7 KB", ModifiedDateFormatted = "2026-03-08 10:14", Icon = ShellIconService.GetFileIcon("image.png") },
            new ExplorerItem { Name = "README.md", FullPath = @"D:\Projects\GameEngine\README.md", IsDirectory = false, Extension = "MD", Size = 4300, SizeFormatted = "4.2 KB", ModifiedDateFormatted = "2026-03-18 12:00", Icon = ShellIconService.GetFileIcon("readme.md") }
        };

        vm.Explorer.SetItemsForDisplay(mockItems);

        // 5. Anonymized Staged Queue Cart
        vm.Config.DestinationDirectory = @"E:\Backups\2026";
        vm.Config.ArchiveName = "GameEngine_Release";
        vm.Config.SelectedFormat = AppBackupFormat.Zip;
        vm.Config.SelectedLevel = AppCompressionLevel.Normal;
        vm.Config.AppendTimestamp = true;
        vm.Config.PreserveDirectoryStructure = true;

        vm.Config.StagedItems.Clear();
        vm.Config.StagedItems.Add(new StagedItem { Name = "src", FullPath = @"D:\Projects\GameEngine\src", Size = 14200000, SizeFormatted = "13.54 MB", IsDirectory = true, Icon = folderIcon });
        vm.Config.StagedItems.Add(new StagedItem { Name = "LevelData.bin", FullPath = @"D:\Projects\GameEngine\LevelData.bin", Size = 28400000, SizeFormatted = "27.08 MB", IsDirectory = false, Icon = ShellIconService.GetFileIcon("file.bin") });
        vm.Config.StagedItems.Add(new StagedItem { Name = "AssetBundle.tar", FullPath = @"C:\Data\AssetBundle.tar", Size = 132000000, SizeFormatted = "125.88 MB", IsDirectory = false, Icon = ShellIconService.GetFileIcon("archive.tar") });
        vm.Config.UpdateStagedStats();

        return vm;
    }
}
