    using IWshRuntimeLibrary;
    using Microsoft.UI.Windowing;
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using Microsoft.UI.Xaml.Media;
    using Microsoft.UI.Xaml.Media.Imaging;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Windows.ApplicationModel.DataTransfer;
    using Windows.Storage;
    using Windows.Storage.Pickers;
    using WinRT.Interop;
    using WinUIEx;
    using File = System.IO.File;

    namespace AppGroup {
        public class ExeFileModel {
            public string? ItemId { get; set; }
            public string FileName { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
            public string? Icon { get; set; }
            public string Tooltip { get; set; } = string.Empty;
            public string Args { get; set; } = string.Empty;
            public string? IconPath { get; set; }
            public string WorkingDirectory { get; set; } = string.Empty;
            public bool RunAsAdministrator { get; set; }
            public string Type { get; set; } = "launch";
            public string? SubgroupId { get; set; }
            public string? AppIdentity { get; set; }
        }

        public sealed partial class EditGroupWindow : WinUIEx.WindowEx {
            public int GroupId { get; private set; }
            private string selectedIconPath = string.Empty;
            private string selectedFilePath = string.Empty;
            private ObservableCollection<ExeFileModel> ExeFiles = new ObservableCollection<ExeFileModel>();
            private bool regularIcon = true;
            private string? lastSelectedItem;
            private string? copiedImagePath;
            private string tempIcon;           // Fix: field is now properly assigned in LoadGroupDataAsync
            private string? groupName;
            private string _stableGroupId = string.Empty;
            private FileSystemWatcher fileWatcher;
            private string groupIdFilePath;
            private int? lastGroupId = null;
            private ExeFileModel CurrentItem { get; set; }
            private string originalItemIconPath = null;
            private bool _isDialogRepositioning = false;
            private bool _isLoadingData = false;  // Fix: guard against concurrent loads

            private const int DEFAULT_LABEL_SIZE = 12;
            private const string DEFAULT_LABEL_POSITION = "Bottom";
        private const string DEFAULT_SORT_MODE = "Manual";
        private IntPtr _hwnd;
            private NativeMethods.SubclassProc _subclassProc;
            private const int SUBCLASS_ID = 3;
            private bool _isFirstActivation = true;
            private bool _wasHidden = true;
      
            public EditGroupWindow(int groupId) {
                this.InitializeComponent();
                _hwnd = WindowNative.GetWindowHandle(this);
                SubclassWindow();
                GroupId = groupId;

                this.CenterOnScreen();

                var iconPath = Path.Combine(AppContext.BaseDirectory, "EditGroup.ico");
                this.AppWindow.SetIcon(iconPath);
            //string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            //string appDataPath = Path.Combine(localAppDataPath, "AppGroup");

            //if (!Directory.Exists(appDataPath))
            //    Directory.CreateDirectory(appDataPath);

            string appDataPath = AppPaths.BaseDataPath;
            ExeListView.ItemsSource = ExeFiles;

                MinHeight = 600;
                MinWidth = 530;
                ExtendsContentIntoTitleBar = true;

                ThemeHelper.UpdateTitleBarColors(this);

                // Fix: initialize fileWatcher before it can be accessed in Closed handler
                SetupFileWatcher();

                _ = LoadGroupDataAsync(GroupId);
                Closed += MainWindow_Closed;
                this.AppWindow.Closing += AppWindow_Closing;

                ApplicationCount.Text = "Item";
                NativeMethods.SetCurrentProcessExplicitAppUserModelID("AppGroup.EditGroup");
                Activated += EditGroupWindow_Activated;
                this.SizeChanged += EditGroupWindow_SizeChanged;
            }

            // Fix: initialize a no-op file watcher so the field is never null
            private void SetupFileWatcher() {
            string watchDir = AppPaths.BaseDataPath;
            //Directory.CreateDirectory(watchDir);
                fileWatcher = new FileSystemWatcher(watchDir) {
                    EnableRaisingEvents = false
                };
            }

            private void SubclassWindow() {
                _subclassProc = new NativeMethods.SubclassProc(SubclassProc);
                NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, SUBCLASS_ID, IntPtr.Zero);
            }

            private IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData) {
                if (msg == NativeMethods.WM_COPYDATA) {
                    try {
                        NativeMethods.COPYDATASTRUCT cds = (NativeMethods.COPYDATASTRUCT)Marshal.PtrToStructure(
                            lParam, typeof(NativeMethods.COPYDATASTRUCT));

                        if (cds.dwData == (IntPtr)100) {
                            string command = Marshal.PtrToStringUni(cds.lpData);

                            if (command.StartsWith("__SHOW_EDIT__|")) {
                                string[] parts = command.Split('|');
                                if (parts.Length == 2 && int.TryParse(parts[1], out int newGroupId)) {
                                    DispatcherQueue.TryEnqueue(async () => {
                                        GroupId = newGroupId;
                                        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_RESTORE);
                                        NativeMethods.SetForegroundWindow(_hwnd);
                                        this.AppWindow.IsShownInSwitchers = true;
                                        await LoadGroupDataAsync(-1);
                                        await Task.Delay(50);
                                        await LoadGroupDataAsync(newGroupId);
                                    });
                                }
                                return (IntPtr)1;
                            }
                        }
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"EditGroupWindow SubclassProc error: {ex.Message}");
                    }
                }
                return NativeMethods.DefSubclassProc(hWnd, msg, wParam, lParam);
            }

            private async void EditGroupWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args) {
                await HideAllDialogsAsync();
            }

            private int GetRefreshIntervalMs(IntPtr hWnd) {
                try {
                    IntPtr monitor = NativeMethods.MonitorFromWindow(hWnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
                    NativeMethods.MONITORINFOEX monitorInfo = new NativeMethods.MONITORINFOEX();
                    monitorInfo.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
                    NativeMethods.GetMonitorInfo(monitor, ref monitorInfo);

                    NativeMethods.DEVMODE devMode = new NativeMethods.DEVMODE();
                    devMode.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
                    if (NativeMethods.EnumDisplaySettings(monitorInfo.szDevice, -1, ref devMode)) {
                        int refreshRate = (int)devMode.dmDisplayFrequency;
                        if (refreshRate > 0) return 1000 / refreshRate;
                    }
                }
                catch { }
                return 16;
            }
        private List<ExeFileModel> GetDisplayOrderedExeFiles() {
            bool alphabetical = (SortModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Alphabetical";
            return alphabetical
                ? ExeFiles.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                : ExeFiles.ToList();
        }
        private void ApplyExeListDisplay() {
            if (ExeListView == null || SortModeComboBox == null) return;   // guard: may fire during InitializeComponent

            bool alphabetical = (SortModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Alphabetical";
            ExeListView.CanReorderItems = !alphabetical;
            ExeListView.ItemsSource = alphabetical
                ? ExeFiles.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList()
                : (IEnumerable<ExeFileModel>)ExeFiles;
        }
        private void SortModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            ApplyExeListDisplay();
            if (!regularIcon && IconGridComboBox.SelectedItem != null)
                CreateGridIcon();
        }
        private void PlayWindowFadeIn() {
                IntPtr hWnd = _hwnd;
                int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE,
                    exStyle | NativeMethods.WS_EX_LAYERED);

                int intervalMs = GetRefreshIntervalMs(hWnd);
                int durationMs = 200;
                int steps = durationMs / intervalMs;
                int currentStep = 0;

                var timer = new System.Threading.Timer(_ => {
                    currentStep++;
                    double t = (double)currentStep / steps;
                    double ease = 1 - Math.Pow(2, -10 * t);
                    byte alpha = (byte)(ease * 255);
                    NativeMethods.SetLayeredWindowAttributes(hWnd, 0, alpha, NativeMethods.LWA_ALPHA);
                    if (currentStep >= steps) {
                        NativeMethods.SetLayeredWindowAttributes(hWnd, 0, 255, NativeMethods.LWA_ALPHA);
                        NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, exStyle);
                    }
                }, null, intervalMs, intervalMs);

                Task.Delay(durationMs + intervalMs * 2).ContinueWith(_ => timer.Dispose());
            }

            private void PlayContentScaleUp() {
                var content = this.Content as FrameworkElement;
                if (content == null) return;

                content.Opacity = 0;
                content.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                content.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 0.95, ScaleY = 0.95 };

                var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

                var fadeAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation {
                    From = 0,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(100)),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase {
                        EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                    }
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fadeAnim, content);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fadeAnim, "Opacity");

                var scaleXAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation {
                    From = 0.95,
                    To = 1.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase {
                        EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                    }
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scaleXAnim, content.RenderTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

                var scaleYAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation {
                    From = 0.95,
                    To = 1.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                    EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase {
                        EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                    }
                };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(scaleYAnim, content.RenderTransform);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

                sb.Children.Add(fadeAnim);
                sb.Children.Add(scaleXAnim);
                sb.Children.Add(scaleYAnim);
                sb.Begin();
            }

            private async void EditGroupWindow_Activated(object sender, WindowActivatedEventArgs e) {
                if (e.WindowActivationState == WindowActivationState.Deactivated) {
                    _ = Task.Run(() => { GC.Collect(0, GCCollectionMode.Optimized); });
                }

                if (e.WindowActivationState == WindowActivationState.CodeActivated) {
                    if (_wasHidden) {
                        PlayContentScaleUp();
                        _wasHidden = false;
                    }

                    int previousGroupId = GroupId;
                    int newGroupId = -1;
                    ExpanderLabel.IsExpanded = false;

                    try {
                    //string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    //string appFolderPath = Path.Combine(appDataPath, "AppGroup");
                    //string filePath = Path.Combine(appFolderPath, "lastEdit");
                    string filePath = Path.Combine(AppPaths.BaseDataPath, "lastEdit");
                    if (File.Exists(filePath)) {
                            string fileGroupIdText = File.ReadAllText(filePath).Trim();
                            if (!string.IsNullOrEmpty(fileGroupIdText) && int.TryParse(fileGroupIdText, out int fileGroupId))
                                newGroupId = fileGroupId;
                        }
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"Error reading group name from file: {ex.Message}");
                    }

                    GroupId = newGroupId;
                    if (GroupId != previousGroupId) {
                        await LoadGroupDataAsync(-1);
                        await LoadGroupDataAsync(GroupId);
                        Debug.WriteLine($"GroupId changed from {previousGroupId} to {GroupId}, data reloaded");
                    }
                    else {
                        Debug.WriteLine($"GroupId unchanged ({GroupId}), skipping data reload");
                    }
                }
            }

            private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args) {
                args.Cancel = true;
                await HideAllDialogsAsync();

                // Fix: do NOT set GroupId = -1 here — it can race with ongoing LoadGroupDataAsync
                NativeMethods.GetCursorPos(out NativeMethods.POINT cursorPos);
                int screenWidth = NativeMethods.GetSystemMetrics(0);
                NativeMethods.SetCursorPos(screenWidth - 1, cursorPos.Y);

                this.Hide();
                _wasHidden = true;
                await Task.Delay(10);

                NativeMethods.SetCursorPos(cursorPos.X, cursorPos.Y);
                this.AppWindow.IsShownInSwitchers = false;
            }

            private async Task HideAllDialogsAsync() {
                var dialogs = FindVisualChildren<ContentDialog>(this.Content);
                foreach (var dialog in dialogs) {
                    if (dialog.Visibility == Visibility.Visible)
                        dialog.Hide();
                }
            }

            private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject {
                if (depObj != null) {
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++) {
                        DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                        if (child != null && child is T)
                            yield return (T)child;
                        foreach (T childOfChild in FindVisualChildren<T>(child))
                            yield return childOfChild;
                    }
                }
            }

            private void MainWindow_Closed(object sender, WindowEventArgs args) {
                fileWatcher?.Dispose();

                // Fix: groupIdFilePath is only non-empty when explicitly set; guard before delete
                if (!string.IsNullOrEmpty(groupIdFilePath) && File.Exists(groupIdFilePath))
                    File.Delete(groupIdFilePath);

                // Fix: tempIcon field (not local variable) is now cleaned up correctly
                if (!string.IsNullOrEmpty(tempIcon)) {
                    try {
                        string tempFolder = Path.GetDirectoryName(tempIcon);
                        if (!string.IsNullOrEmpty(tempFolder) && Directory.Exists(tempFolder))
                            Directory.Delete(tempFolder, true);
                    }
                    catch (Exception ex) {
                        Debug.WriteLine($"Failed to clean up temp icon: {ex.Message}");
                    }
                }
            }

            private void ExeListView_DragOver(object sender, DragEventArgs e) {
                e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
                    ? DataPackageOperation.Copy | DataPackageOperation.Link
                    : DataPackageOperation.None;
            }

            private async void ExeListView_DragEnter(object sender, DragEventArgs e) {
                try {
                    e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
                        ? DataPackageOperation.Copy | DataPackageOperation.Link
                        : DataPackageOperation.None;
                }
                catch (Exception ex) {
                    Debug.WriteLine($"Drag Enter Error: {ex.Message}");
                }
            }

            private async void ExeListView_Drop(object sender, DragEventArgs e) {
                try {
                    if (e.DataView.Contains(StandardDataFormats.StorageItems)) {
                        var items = await e.DataView.GetStorageItemsAsync();
                        foreach (var item in items) {
                            if (item is StorageFile file &&
                                (file.FileType.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                                 file.FileType.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                 file.FileType.Equals(".url", StringComparison.OrdinalIgnoreCase))) {

                                // Resolve DragTemp .lnk back to the real Groups path
                                string resolvedPath = file.Path;
                                if (file.FileType.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
                                string dragTempDir = Path.Combine(AppPaths.BaseDataPath, "DragTemp");
                                if (resolvedPath.StartsWith(dragTempDir, StringComparison.OrdinalIgnoreCase)) {
                                    string groupsDir = Path.Combine(AppPaths.BaseDataPath, "Groups");
                                    string fileName = Path.GetFileNameWithoutExtension(file.Path);
                                        string realPath = Path.Combine(groupsDir, fileName, $"{fileName}.lnk");
                                        if (File.Exists(realPath))
                                            resolvedPath = realPath;
                                    }
                                }

                                string icon;
                                if (file.FileType.Equals(".url", StringComparison.OrdinalIgnoreCase))
                                    icon = await IconHelper.GetUrlFileIconAsync(resolvedPath);
                                else
                                    icon = await IconCache.GetIconPathAsync(resolvedPath);

                                if (string.IsNullOrWhiteSpace(icon) || !File.Exists(icon))
                                    icon = await IconCache.GetIconPathAsync(resolvedPath);

                                ExeFiles.Add(new ExeFileModel {
                                    FileName = Path.GetFileName(resolvedPath),
                                    Icon = icon,
                                    FilePath = resolvedPath,
                                    IconPath = icon
                                });
                            }
                      
                        }
                        RefreshListViewState();
                    }
                }
                catch (Exception ex) {
                    Debug.WriteLine($"Drop Error: {ex.Message}");
                }
            }
            private void BrowseFilesClick(object sender, RoutedEventArgs e) => BrowseFiles();

      
            // Fix: extracted shared list-view refresh logic to avoid duplication and bugs
            private void RefreshListViewState() {
            //ExeListView.ItemsSource = ExeFiles;
            ApplyExeListDisplay();
            lastSelectedItem = GroupColComboBox.SelectedItem as string;

                ApplicationCount.Text = ExeListView.Items.Count > 1
                    ? ExeListView.Items.Count + " Items"
                    : ExeListView.Items.Count == 1 ? "1 Item" : "";

                IconGridComboBox.Items.Clear();
                IconGridComboBox.Items.Add("2");
                if (ExeFiles.Count >= 9)
                    IconGridComboBox.Items.Add("3");
                IconGridComboBox.SelectedItem = "2";

                GroupColComboBox.Items.Clear();
                for (int i = 1; i <= ExeFiles.Count; i++)
                    GroupColComboBox.Items.Add(i.ToString());

                if (ExeFiles.Count > 3)
                    GroupColComboBox.SelectedItem = lastSelectedItem ?? "3";
                else
                    GroupColComboBox.SelectedItem = ExeFiles.Count.ToString();

                if (!regularIcon) {
                    IconGridComboBox.Visibility = Visibility.Visible;
                    if (CustomDialog.XamlRoot != null)
                        CustomDialog.Hide();
                }
            }

            private void EnforceLayoutConstraints() {
                bool headerOff = !GroupHeader.IsOn;
                bool singleColumn = GroupColComboBox.SelectedItem?.ToString() == "1";
                LayoutComboBox.IsEnabled = !(headerOff || singleColumn);
            }

            private void GroupColComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
                if (GroupColComboBox.SelectedItem?.ToString() == "1") {
                    GroupHeader.IsEnabled = false;
                    HeaderPanel.Opacity = 0.5;
                }
                else {
                    GroupHeader.IsEnabled = true;
                    HeaderPanel.Opacity = 1.0;
                }
                EnforceLayoutConstraints();
            }

            private void GroupHeader_Toggled(object sender, RoutedEventArgs e) {
                HeaderPositionComboBox.IsEnabled = GroupHeader.IsOn;
                HeaderPositionPanel.Opacity = GroupHeader.IsOn ? 1.0 : 0.5;
                EnforceLayoutConstraints();
            }

            private void ShowLabels_Toggled(object sender, RoutedEventArgs e) {
                bool on = ShowLabels.IsOn;
                LabelSizePanel.Opacity = on ? 1.0 : 0.5;
                LabelSizeComboBox.IsEnabled = on;
                LabelPositionPanel.Opacity = on ? 1.0 : 0.5;
                LabelPositionComboBox.IsEnabled = on;
            }

            private void InitializeLabelSizeComboBox() {
                LabelSizeComboBox.Items.Clear();
                foreach (int size in new[] { 8, 9, 10, 11, 12, 14 })
                    LabelSizeComboBox.Items.Add(size.ToString());
                LabelSizeComboBox.SelectedItem = DEFAULT_LABEL_SIZE.ToString();
            }

            private void InitializeLabelPositionComboBox() {
                LabelPositionComboBox.Items.Clear();
                LabelPositionComboBox.Items.Add("Right");
                LabelPositionComboBox.Items.Add("Bottom");
                LabelPositionComboBox.SelectedItem = DEFAULT_LABEL_POSITION;
            }

            private async Task LoadGroupDataAsync(int groupId) {
                // Fix: prevent concurrent loads from stacking up
                if (_isLoadingData) return;
                _isLoadingData = true;

                try {
                    await Task.Run(async () => {
                        string jsonFilePath = JsonConfigHelper.GetDefaultConfigPath();
                        if (!File.Exists(jsonFilePath)) {
                            ResetUIToDefaults();
                            return;
                        }

                        string jsonContent = await JsonConfigHelper.ReadJsonFromFileAsync(jsonFilePath);
                        JsonNode jsonObject = JsonNode.Parse(jsonContent) ?? new JsonObject();

                        if (jsonObject.AsObject().TryGetPropertyValue(groupId.ToString(), out JsonNode groupNode)) {
                            string gName = groupNode["groupName"]?.GetValue<string>() ?? string.Empty;
                            string stableGroupId = groupNode["id"]?.GetValue<string>() ?? string.Empty;
                            int groupCol = groupNode["groupCol"]?.GetValue<int>() ?? 0;
                            bool showOnTray = groupNode["showOnTray"]?.GetValue<bool>() ?? false;

                            string groupIcon = IconHelper.FindOrigIcon(groupNode["groupIcon"]?.GetValue<string>());
                            bool groupHeader = groupNode["groupHeader"]?.GetValue<bool>() ?? false;
                            bool showLabels = groupNode["showLabels"]?.GetValue<bool>() ?? false;
                            int labelSize = groupNode["labelSize"]?.GetValue<int>() ?? DEFAULT_LABEL_SIZE;
                            string labelPosition = groupNode["labelPosition"]?.GetValue<string>() ?? DEFAULT_LABEL_POSITION;
                            JsonArray items = groupNode["items"]?.AsArray() ?? new JsonArray();
                            string headerPosition = groupNode["headerPosition"]?.GetValue<string>() ?? "Top";
                            string layout = groupNode["layout"]?.GetValue<string>() ?? "Default";
                            string sortMode = groupNode["sortMode"]?.GetValue<string>() ?? DEFAULT_SORT_MODE;
                            // Fix: guard File.Copy — only copy if the icon file actually exists
                            string resolvedTempIcon = null;
                            if (!string.IsNullOrEmpty(groupIcon) && File.Exists(groupIcon)) {
                                string tempSubfolderPath = Path.Combine(Path.GetTempPath(), "AppGroup");
                                Directory.CreateDirectory(tempSubfolderPath);
                                string uniqueFolderName = new DirectoryInfo(Path.GetDirectoryName(groupIcon)).Name;
                                string uniqueFolderPath = Path.Combine(tempSubfolderPath, uniqueFolderName);
                                Directory.CreateDirectory(uniqueFolderPath);
                                resolvedTempIcon = Path.Combine(uniqueFolderPath, Path.GetFileName(groupIcon));
                                File.Copy(groupIcon, resolvedTempIcon, overwrite: true);
                            }

                            DispatcherQueue.TryEnqueue(() => {
                                // Fix: assign to the field, not a new local — so MainWindow_Closed can clean it up
                                tempIcon = resolvedTempIcon;
                                groupName = gName;
                                _stableGroupId = stableGroupId;

                                ShowOnTray.IsOn = showOnTray;
                                GroupHeader.IsOn = groupHeader;
                                if (!string.IsNullOrEmpty(gName))
                                    GroupNameTextBox.Text = gName;

                                InitializeLabelSizeComboBox();
                                InitializeLabelPositionComboBox();
                                ShowLabels.IsOn = showLabels;
                                LabelSizeComboBox.SelectedItem = labelSize.ToString();
                                LabelPositionComboBox.SelectedItem = labelPosition;

                                LabelSizePanel.Opacity = showLabels ? 1.0 : 0.5;
                                LabelSizeComboBox.IsEnabled = showLabels;
                                LabelPositionPanel.Opacity = showLabels ? 1.0 : 0.5;
                                LabelPositionComboBox.IsEnabled = showLabels;

                                HeaderPositionComboBox.SelectedItem = HeaderPositionComboBox.Items
                                    .OfType<ComboBoxItem>()
                                    .FirstOrDefault(i => i.Content.ToString() == headerPosition);

                                LayoutComboBox.SelectedItem = LayoutComboBox.Items
                                    .OfType<ComboBoxItem>()
                                    .FirstOrDefault(i => i.Content.ToString() == layout);

                                SortModeComboBox.SelectedItem = SortModeComboBox.Items
    .OfType<ComboBoxItem>()
    .FirstOrDefault(i => i.Content.ToString() == sortMode);
                                // Fix: clear ExeFiles before adding on reload to avoid accumulation
                                ExeFiles.Clear();
                            });

                            if (groupCol > 0) {
                                DispatcherQueue.TryEnqueue(() => {
                                    GroupColComboBox.Items.Clear();
                                    for (int i = 1; i <= items.Count; i++)
                                        GroupColComboBox.Items.Add(i.ToString());
                                    GroupColComboBox.SelectedItem = groupCol.ToString();
                                });
                            }

                            if (!string.IsNullOrEmpty(resolvedTempIcon)) {
                                DispatcherQueue.TryEnqueue(() => {
                                    selectedIconPath = resolvedTempIcon;
                                    IconPreviewImage.Source = new BitmapImage(new Uri(resolvedTempIcon));
                                    IconPreviewBorder.Visibility = Visibility.Visible;
                                    ApplicationCount.Text = items.Count > 1 ? items.Count + " Items"
                                        : items.Count == 1 ? "1 Item" : "";
                                });
                            }

                            foreach (JsonNode? itemNode in items) {
                                if (itemNode is not JsonObject item) continue;
                                string filePath = item["target"]?.GetValue<string>() ?? string.Empty;
                                string itemId = item["id"]?.GetValue<string>() ?? string.Empty;
                                string displayName = item["displayName"]?.GetValue<string>()
                                    ?? item["tooltip"]?.GetValue<string>()
                                    ?? Path.GetFileName(filePath);
                                string arguments = item["arguments"]?.GetValue<string>()
                                    ?? item["args"]?.GetValue<string>()
                                    ?? string.Empty;
                                string? icon = item["icon"]?.GetValue<string>();
                                bool targetExists = !string.IsNullOrWhiteSpace(filePath) &&
                                    (File.Exists(filePath) || Directory.Exists(filePath));
                                if ((string.IsNullOrWhiteSpace(icon) || !File.Exists(icon)) && targetExists) {
                                    icon = filePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                                        ? await IconHelper.GetUrlFileIconAsync(filePath)
                                        : await IconCache.GetIconPathAsync(filePath);
                                }

                                await Task.Delay(10);
                                DispatcherQueue.TryEnqueue(() => {
                                    ExeFiles.Add(new ExeFileModel {
                                        ItemId = itemId,
                                        FileName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(filePath) : displayName,
                                        Icon = icon,
                                        FilePath = filePath,
                                        Tooltip = displayName,
                                        Args = arguments,
                                        IconPath = icon,
                                        WorkingDirectory = item["workingDirectory"]?.GetValue<string>() ?? string.Empty,
                                        RunAsAdministrator = item["runAsAdministrator"]?.GetValue<bool>() ?? false,
                                        Type = item["type"]?.GetValue<string>() ?? "launch",
                                        SubgroupId = item["subgroupId"]?.GetValue<string>(),
                                        AppIdentity = item["appIdentity"]?.GetValue<string>()
                                    });
                                });
                            }

                            DispatcherQueue.TryEnqueue(() => {
                                IconGridComboBox.Items.Clear();
                                IconGridComboBox.Items.Add("2");
                                if (ExeFiles.Count >= 9) IconGridComboBox.Items.Add("3");
                                IconGridComboBox.SelectedItem = "2";
                                if (!string.IsNullOrEmpty(groupIcon) && groupIcon.Contains("grid")) {
                                    IconGridComboBox.SelectedItem = groupIcon.Contains("grid3") ? "3" : "2";
                                    regularIcon = false;
                                    IconGridComboBox.Visibility = Visibility.Visible;
                                }
                                ApplyExeListDisplay();
                            });
                        }
                        else {
                            ResetUIToDefaults();
                        }
                    });
                }
                finally {
                    _isLoadingData = false;

                    DispatcherQueue.TryEnqueue(() => {
                        if (CustomDialog != null && CustomDialog.XamlRoot == null)
                            CustomDialog.XamlRoot = this.Content.XamlRoot;
                    });
                }
            }

            private void ResetUIToDefaults() {
                DispatcherQueue.TryEnqueue(() => {
                    groupName = "";
                    _stableGroupId = AppGroupConfigSchema.NewStableId();
                    GroupHeader.IsOn = false;
                    GroupNameTextBox.Text = string.Empty;
                    GroupColComboBox.Items.Clear();
                    selectedIconPath = string.Empty;
                    IconPreviewImage.Source = new BitmapImage(new Uri("ms-appx:///default_preview.png"));
                    ApplicationCount.Text = string.Empty;
                    ExeFiles.Clear();
                    SortModeComboBox.SelectedItem = SortModeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content.ToString() == DEFAULT_SORT_MODE);
                    ApplyExeListDisplay();
                    IconGridComboBox.Items.Clear();
                    IconGridComboBox.Visibility = Visibility.Collapsed;

                    InitializeLabelSizeComboBox();
                    InitializeLabelPositionComboBox();

                    ShowLabels.IsOn = false;
                    ShowOnTray.IsOn = false;
                    LabelSizePanel.Opacity = 0.5;
                    LabelSizeComboBox.IsEnabled = false;
                    LabelPositionPanel.Opacity = 0.5;
                    LabelPositionComboBox.IsEnabled = false;
                });
            }

            private async void CreateGridIcon() {
                var selectedItem = IconGridComboBox.SelectedItem;
                if (selectedItem != null && int.TryParse(selectedItem.ToString(), out int selectedSize)) {
                var selectedItems = GetDisplayOrderedExeFiles().Take(selectedSize * selectedSize).ToList();   // was: ExeFiles.Take(...)

                //var selectedItems = ExeFiles.Take(selectedSize * selectedSize).ToList();
                try {
                        IconHelper iconHelper = new IconHelper();
                        selectedIconPath = await iconHelper.CreateGridIconAsync(
                            selectedItems, selectedSize, IconPreviewImage, IconPreviewBorder);
                    }
                    catch (Exception ex) {
                        ShowErrorDialog("Error creating grid icon", ex.Message);
                        Debug.WriteLine($"Grid icon creation error: {ex.Message}");
                    }
                }
                else {
                    ShowErrorDialog("Invalid grid size", "Please select a valid grid size from the ComboBox.");
                }
            }

            private void IconGridComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
                if (IconGridComboBox.SelectedItem != null && !regularIcon)
                    CreateGridIcon();
            }

            private async void BrowseIconButton_Click(object sender, RoutedEventArgs e) {
                await CustomDialog.ShowAsync();
            }

            private void CloseDialog(object sender, RoutedEventArgs e) => CustomDialog.Hide();
            private void CloseEditDialog(object sender, RoutedEventArgs e) => EditItemDialog.Hide();
            private void CloseCustomizeDialog(object sender, RoutedEventArgs e) => CustomizeDialog.Hide();

            private void GridClick(object sender, RoutedEventArgs e) {
                regularIcon = false;
                if (ExeListView.Items.Count == 0)
                    BrowseFiles();
                else {
                    CustomDialog.Hide();
                    IconGridComboBox.Visibility = Visibility.Visible;
                    CreateGridIcon();
                }
            }

            private void RegularClick(object sender, RoutedEventArgs e) {
                regularIcon = true;
                BrowseIcon();
            }

            private async void BrowseIcon() {
                try {
                    FileOpenPicker openPicker = new FileOpenPicker();
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    InitializeWithWindow.Initialize(openPicker, hwnd);
                    openPicker.FileTypeFilter.Add(".jpeg");
                    openPicker.FileTypeFilter.Add(".jpg");
                    openPicker.FileTypeFilter.Add(".exe");
                    openPicker.FileTypeFilter.Add(".url");
                    openPicker.FileTypeFilter.Add(".png");
                    openPicker.FileTypeFilter.Add(".ico");
                    StorageFile file = await openPicker.PickSingleFileAsync();

                    if (file != null) {
                        selectedIconPath = file.Path;
                        BitmapImage bitmapImage = new BitmapImage();
                        string iconPath;

                        if (file.FileType.Equals(".url", StringComparison.OrdinalIgnoreCase)) {
                            iconPath = await IconHelper.GetUrlFileIconAsync(file.Path);
                        }
                        else if (file.FileType == ".exe") {
                            iconPath = await IconCache.GetIconPathAsync(file.Path);
                            if (!string.IsNullOrEmpty(iconPath)) {
                                using var stream = File.OpenRead(iconPath);
                                await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                            }
                        }
                        else {
                            using var stream = await file.OpenReadAsync();
                            await bitmapImage.SetSourceAsync(stream);
                        }

                        IconPreviewImage.Source = bitmapImage;
                        IconPreviewBorder.Visibility = Visibility.Visible;

                        if (CustomDialog.XamlRoot != null) {
                            CustomDialog.Hide();
                            IconGridComboBox.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                catch (Exception ex) {
                    ShowErrorDialog("Error selecting icon", ex.Message);
                }
            }


            private async void BrowseFiles() {
                var openPicker = new FileOpenPicker();
                openPicker.SuggestedStartLocation = PickerLocationId.Desktop;
                openPicker.FileTypeFilter.Add(".exe");
                openPicker.FileTypeFilter.Add(".url");
                openPicker.FileTypeFilter.Add(".lnk");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                InitializeWithWindow.Initialize(openPicker, hwnd);

                var files = await openPicker.PickMultipleFilesAsync();
                if (files == null || files.Count == 0) return;

                foreach (var file in files) {
                    // After
                    string icon;
                    if (file.FileType.Equals(".url", StringComparison.OrdinalIgnoreCase))
                        icon = await IconHelper.GetUrlFileIconAsync(file.Path);
                    else if (file.FileType.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) {
                        icon = await IconCache.GetIconPathAsync(file.Path);
                        if (string.IsNullOrWhiteSpace(icon) || !File.Exists(icon))
                            icon = await IconCache.GetIconPathAsync(file.Path); // retry via lnk target
                    }
                    else
                        icon = await IconCache.GetIconPathAsync(file.Path);

                    // Final fallback: extract from file path directly if still empty
                    if (string.IsNullOrWhiteSpace(icon) || !File.Exists(icon))
                        icon = await IconCache.GetIconPathAsync(file.Path);

                    ExeFiles.Add(new ExeFileModel {
                        FileName = file.Name,
                        Icon = icon,
                        FilePath = file.Path,
                        IconPath = icon,
                        Tooltip = "",
                        Args = ""
                    });
                }

                RefreshListViewState();
            }

            private void ExeListView_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

            private void ExeListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) {
                if (args.DropResult == DataPackageOperation.Move
                    && IconGridComboBox.SelectedItem != null && !regularIcon)
                    CreateGridIcon();
            }

            private async void CustomizeDialog_Click(object sender, RoutedEventArgs e) {
                await CustomizeDialog.ShowAsync();
            }

            private async void EditItem_Click(object sender, RoutedEventArgs e) {
                if (sender is Button button && button.Tag is ExeFileModel item) {
                    CurrentItem = item;
                    EditTitle.Text = item.FileName;
                    TooltipTextBox.Text = item.Tooltip;
                    ArgsTextBox.Text = item.Args;

                //string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                //string appDataPath = Path.Combine(localAppDataPath, "AppGroup");
                //string groupsFolder = Path.Combine(appDataPath, "Groups");
                //Directory.CreateDirectory(groupsFolder);
                if (string.IsNullOrWhiteSpace(_stableGroupId))
                    _stableGroupId = AppGroupConfigSchema.NewStableId();
                string groupFolder = JsonConfigHelper.GetGroupFolderPath(_stableGroupId);
                Directory.CreateDirectory(groupFolder);
                currentGroupPath = Path.Combine(groupFolder, "Assets");

                    originalItemIconPath = await IconCache.GetIconPathAsync(item.FilePath);

                    if (!string.IsNullOrEmpty(item.IconPath) && item.IconPath != originalItemIconPath) {
                        selectedItemIconPath = item.IconPath;
                        ItemIconPreview.Source = new BitmapImage(new Uri(item.IconPath));
                    }
                    else {
                        selectedItemIconPath = originalItemIconPath;
                        if (!string.IsNullOrEmpty(originalItemIconPath))
                            ItemIconPreview.Source = new BitmapImage(new Uri(originalItemIconPath));
                    }

                    await EditItemDialog.ShowAsync();
                }
            }

            private void EditItemSave_Click(object sender, RoutedEventArgs e) {
                if (CurrentItem != null) {
                    CurrentItem.Tooltip = TooltipTextBox.Text;
                    CurrentItem.Args = ArgsTextBox.Text;

                    if (!string.IsNullOrEmpty(selectedItemIconPath)) {
                        if (selectedItemIconPath == originalItemIconPath) {
                            CurrentItem.IconPath = null;
                            CurrentItem.Icon = originalItemIconPath;
                        }
                        else {
                            CurrentItem.IconPath = selectedItemIconPath;
                            CurrentItem.Icon = selectedItemIconPath;
                        }
                    }

                    int index = ExeFiles.IndexOf(CurrentItem);
                    if (index >= 0) {
                        ExeFiles.RemoveAt(index);
                        ExeFiles.Insert(index, CurrentItem);
                    }

                    if (!regularIcon && IconGridComboBox.SelectedItem != null)
                        CreateGridIcon();
                ApplyExeListDisplay();
                EditItemDialog.Hide();
                }
            }

            private async void ResetItemIcon_Click(object sender, RoutedEventArgs e) {
                try {
                    if (!string.IsNullOrEmpty(originalItemIconPath)) {
                        selectedItemIconPath = originalItemIconPath;
                        ItemIconPreview.Source = new BitmapImage(new Uri(originalItemIconPath));
                    }
                    else if (CurrentItem != null) {
                        string originalIcon = await IconCache.GetIconPathAsync(CurrentItem.FilePath);
                        if (!string.IsNullOrEmpty(originalIcon)) {
                            selectedItemIconPath = originalIcon;
                            originalItemIconPath = originalIcon;
                            ItemIconPreview.Source = new BitmapImage(new Uri(originalIcon));
                        }
                    }
                }
                catch (Exception ex) {
                    var dialog = new ContentDialog {
                        Title = "Error",
                        Content = $"Failed to reset icon: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }

            private void RemoveItem_Click(object sender, RoutedEventArgs e) {
                if (sender is Button button && button.Tag is ExeFileModel item)
                    ExeFiles.Remove(item);

            //ExeListView.ItemsSource = ExeFiles;
            ApplyExeListDisplay();
            ApplicationCount.Text = ExeListView.Items.Count > 0
                    ? ExeListView.Items.Count + " Items" : "Item";

                IconGridComboBox.Items.Clear();
                IconGridComboBox.Items.Add("2");
                if (ExeFiles.Count >= 9)
                    IconGridComboBox.Items.Add("3");
                IconGridComboBox.SelectedItem = "2";

                lastSelectedItem = GroupColComboBox.SelectedItem as string;
                GroupColComboBox.Items.Clear();
                for (int i = 1; i <= ExeFiles.Count; i++)
                    GroupColComboBox.Items.Add(i.ToString());

                if (lastSelectedItem != null && int.TryParse(lastSelectedItem, out int lastIdx)) {
                    GroupColComboBox.SelectedItem = lastIdx > ExeFiles.Count
                        ? ExeFiles.Count.ToString()
                        : lastSelectedItem;
                }
            }

            private void GroupNameTextBox_GotFocus(object sender, RoutedEventArgs e) { }

            private void GroupNameTextBox_TextChanged(object sender, TextChangedEventArgs e) {
                if (sender is TextBox textBox) {
                    string newGroupName = textBox.Text;
                    string oldGroupName = GetOldGroupName();
                    RenameInfoBar.IsOpen = !string.IsNullOrEmpty(GroupNameTextBox.Text)
                        && !string.IsNullOrEmpty(oldGroupName)
                        && oldGroupName != newGroupName;
                }
            }

            private async void CreateShortcut_Click(object sender, RoutedEventArgs e) {
                var button = sender as Button;
                if (button != null && !button.IsEnabled) return;
                if (button != null) button.IsEnabled = false;

                try {
                    string newGroupName = GroupNameTextBox.Text?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(newGroupName)) {
                        await ShowDialog("Error", "Please enter a group name.");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(selectedIconPath)) {
                        await ShowDialog("Error", "Please select an icon.");
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(_stableGroupId))
                        _stableGroupId = AppGroupConfigSchema.NewStableId();

                    string headerPosition = (HeaderPositionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Top";
                    string layout = (LayoutComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Default";
                    string sortMode = (SortModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DEFAULT_SORT_MODE;
                    string groupFolder = JsonConfigHelper.GetGroupFolderPath(_stableGroupId);
                    string assetFolder = Path.Combine(groupFolder, "Assets");
                    Directory.CreateDirectory(assetFolder);
                    File.SetAttributes(assetFolder, File.GetAttributes(assetFolder) | FileAttributes.Hidden);

                    string iconBaseName = regularIcon ? "group_regular"
                        : IconGridComboBox.SelectedItem?.ToString() == "3" ? "group_grid3" : "group_grid";
                    string icoFilePath = Path.Combine(assetFolder, iconBaseName + ".ico");
                    string originalImageExtension = Path.GetExtension(selectedIconPath);
                    if (originalImageExtension.Equals(".ico", StringComparison.OrdinalIgnoreCase)) {
                        File.Copy(selectedIconPath, icoFilePath, true);
                    }
                    else if (originalImageExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) {
                        string extractedPngPath = await IconCache.GetIconPathAsync(selectedIconPath);
                        if (string.IsNullOrEmpty(extractedPngPath)) {
                            await ShowDialog("Error", "Failed to extract icon from EXE file.");
                            return;
                        }
                        string pngFilePath = Path.Combine(assetFolder, iconBaseName + ".png");
                        File.Copy(extractedPngPath, pngFilePath, true);
                        if (!await IconHelper.ConvertToIco(pngFilePath, icoFilePath)) {
                            await ShowDialog("Error", "Failed to convert extracted PNG to ICO format.");
                            return;
                        }
                    }
                    else if (!await IconHelper.ConvertToIco(selectedIconPath, icoFilePath)) {
                        await ShowDialog("Error", "Failed to convert image to ICO format.");
                        return;
                    }

                    if (!originalImageExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) {
                        copiedImagePath = Path.Combine(assetFolder, iconBaseName + originalImageExtension);
                        if (!string.Equals(Path.GetFullPath(selectedIconPath), Path.GetFullPath(copiedImagePath), StringComparison.OrdinalIgnoreCase))
                            File.Copy(selectedIconPath, copiedImagePath, true);
                    }

                    string oldGroupName = GetOldGroupName();
                    JsonConfigHelper.CreateOrUpdateGroupShortcut(_stableGroupId, newGroupName, icoFilePath);
                    bool isPinned = await TaskbarManager.IsShortcutPinnedToTaskbar(_stableGroupId, oldGroupName);
                    if (isPinned) {
                        await TaskbarManager.UpdateTaskbarShortcutIcon(_stableGroupId, oldGroupName, newGroupName, icoFilePath);
                        TaskbarManager.TryRefreshTaskbarWithoutRestartAsync();
                    }

                    bool groupHeader = GroupHeader.IsEnabled && GroupHeader.IsOn;
                    if (GroupColComboBox.SelectedItem == null ||
                        !int.TryParse(GroupColComboBox.SelectedItem.ToString(), out int groupCol) || groupCol <= 0) {
                        await ShowDialog("Error", "Please select a valid group column value.");
                        return;
                    }

                    List<PersistedGroupItem> persistedItems = ExeFiles.Select(item => new PersistedGroupItem(
                        item.ItemId, item.FilePath, item.Tooltip, item.Args, item.IconPath ?? item.Icon ?? string.Empty,
                        item.WorkingDirectory, item.RunAsAdministrator, item.Type, item.SubgroupId, item.AppIdentity)).ToList();

                    bool showLabels = ShowLabels.IsOn;
                    int labelSize = LabelSizeComboBox.SelectedItem != null
                        ? int.Parse(LabelSizeComboBox.SelectedItem.ToString()) : DEFAULT_LABEL_SIZE;
                    string labelPosition = LabelPositionComboBox.SelectedItem?.ToString() ?? DEFAULT_LABEL_POSITION;
                    JsonConfigHelper.SaveGroupToJson(
                        JsonConfigHelper.GetDefaultConfigPath(), GroupId, _stableGroupId, newGroupName,
                        groupHeader, icoFilePath, groupCol, showLabels, labelSize, labelPosition, headerPosition,
                        layout, ShowOnTray.IsOn, sortMode, persistedItems);
                    GroupTrayManager.SyncFromJson();

                    if (!string.IsNullOrEmpty(tempIcon) && File.Exists(tempIcon)) {
                        try { File.Delete(tempIcon); } catch (Exception ex) { Debug.WriteLine(ex.Message); }
                        tempIcon = null;
                    }

                    IntPtr hWnd = NativeMethods.FindWindow(null, "App Group");
                    if (hWnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hWnd);
                    GroupId = -1;
                    Hide();
                    _wasHidden = true;
                }
                catch (Exception ex) {
                    await ShowDialog("Error", $"An error occurred: {ex.Message}");
                }
                finally {
                    if (button != null) button.IsEnabled = true;
                }
            }

            private string GetOldGroupName() => groupName ?? "";
        private bool GroupNameExists(string candidateName, int excludeGroupId) {
            string jsonFilePath = JsonConfigHelper.GetDefaultConfigPath();
            if (!File.Exists(jsonFilePath)) return false;

            JsonNode jsonObject = JsonNode.Parse(File.ReadAllText(jsonFilePath)) ?? new JsonObject();
            foreach (var kvp in jsonObject.AsObject()) {
                if (!int.TryParse(kvp.Key, out int existingGroupId) || existingGroupId == excludeGroupId)
                    continue;

                string existingName = kvp.Value?["groupName"]?.GetValue<string>();
                if (string.Equals(existingName, candidateName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        private void SaveGroupIdToFile(string groupId) {
                try {
                string filePath = Path.Combine(AppPaths.BaseDataPath, "lastEdit");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? "");
                File.WriteAllText(filePath, groupId);
            }
                catch (Exception ex) {
                    Debug.WriteLine($"Failed to save group ID: {ex.Message}");
                }
            }

            private string selectedItemIconPath = null;
            private string currentGroupPath = null;

            private async void BrowseItemIcon_Click(object sender, RoutedEventArgs e) {
                var picker = new FileOpenPicker();
                picker.ViewMode = PickerViewMode.Thumbnail;
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeFilter.Add(".ico");
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".exe");

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                    await ProcessSelectedIcon(file);
            }

            private async Task ProcessSelectedIcon(StorageFile file) {
                try {
                    if (!string.IsNullOrEmpty(currentGroupPath) && !Directory.Exists(currentGroupPath))
                        Directory.CreateDirectory(currentGroupPath);

                    selectedItemIconPath = file.Path;
                    BitmapImage bitmapImage = new BitmapImage();

                    if (file.FileType == ".exe") {
                        var iconPath = await IconCache.GetIconPathAsync(file.Path);
                        if (!string.IsNullOrEmpty(iconPath)) {
                            using var stream = File.OpenRead(iconPath);
                            await bitmapImage.SetSourceAsync(stream.AsRandomAccessStream());
                            selectedItemIconPath = iconPath;
                        }
                    }
                    else {
                        using var stream = await file.OpenReadAsync();
                        await bitmapImage.SetSourceAsync(stream);
                    }

                    ItemIconPreview.Source = bitmapImage;
                }
                catch (Exception ex) {
                    var dialog = new ContentDialog {
                        Title = "Error",
                        Content = $"Failed to process icon: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }
            }

            private async Task<bool> ConfirmOverwrite(string path) {
                ContentDialog dialog = new ContentDialog {
                    Title = "Overwrite",
                    Content = "A shortcut with this name already exists. Do you want to replace it?",
                    PrimaryButtonText = "Yes",
                    CloseButtonText = "No",
                    XamlRoot = Content.XamlRoot
                };
                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            }

            private async Task ShowDialog(string title, string message) {
                ContentDialog dialog = new ContentDialog {
                    Title = title,
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await dialog.ShowAsync();
            }

            private async void ShowErrorDialog(string title, string message) => await ShowDialog(title, message);

            [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
            private interface IShellLinkW {
                void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
                void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
                void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
                void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            }

            [ClassInterface(ClassInterfaceType.None)]
            [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
            private class CShellLink { }
        }
    }