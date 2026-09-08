from __future__ import annotations

from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count}")
    return text.replace(old, new, 1)


def regex_once(text: str, pattern: str, replacement: str, label: str, flags: int = re.S) -> str:
    result, count = re.subn(pattern, replacement, text, count=1, flags=flags)
    if count != 1:
        raise RuntimeError(f"{label}: expected one regex match, found {count}")
    return result


# Pure schema compile fix.
path = "AppGroup/StableIdentity.cs"
text = read(path)
text = replace_once(
    text,
    'if (versionNode is JsonValue && versionNode.TryGetValue<int>(out int version) && version >= 1)',
    'if (versionNode is JsonValue versionValue && versionValue.TryGetValue<int>(out int version) && version >= 1)',
    "JsonValue version read",
)
write(path, text)

# File-backed persistence bridge and canonical duplicate-safe save API.
path = "AppGroup/JsonConfigHelper.cs"
text = read(path)
if "using System.Reflection;" not in text:
    text = text.replace("using System.Linq;\n", "using System.Linq;\nusing System.Reflection;\n", 1)

anchor = '''        public static JsonObject ReadCurrentRoot()\n        {\n            EnsureCurrentSchema();\n            return AppGroupConfigSchema.ParseCurrent(File.ReadAllText(GetDefaultConfigPath()));\n        }\n'''
insert = anchor + '''\n        public static string GetGroupsOnlyJson(string json)\n        {\n            JsonObject root = AppGroupConfigSchema.ParseCurrent(json);\n            JsonObject groups = new();\n            foreach ((string slot, JsonObject group) in AppGroupConfigSchema.EnumerateGroups(root))\n            {\n                groups[slot] = group.DeepClone();\n            }\n            return groups.ToJsonString();\n        }\n\n        public static void WriteCurrentRoot(JsonObject root)\n        {\n            root[AppGroupConfigSchema.VersionProperty] = AppGroupConfigSchema.CurrentVersion;\n            MigrationResult validated = AppGroupConfigSchema.Migrate(root.ToJsonString(IndentedJson));\n            WriteAtomically(GetDefaultConfigPath(), validated.Json);\n        }\n'''
text = replace_once(text, anchor, insert, "JsonConfigHelper read-root bridge")

save_anchor = '''        public static void AddGroupToJson(\n            string filePath,\n'''
save_method = '''        public static void SaveGroupToJson(\n            string filePath,\n            int groupSlot,\n            string stableGroupId,\n            string groupName,\n            bool groupHeader,\n            string groupIcon,\n            int groupCol,\n            bool showLabels,\n            int labelSize,\n            string labelPosition,\n            string headerPosition,\n            string layout,\n            bool showOnTray,\n            string sortMode,\n            IReadOnlyList<PersistedGroupItem> items)\n        {\n            EnsureCurrentSchema(filePath);\n            JsonObject root = AppGroupConfigSchema.ParseCurrent(File.ReadAllText(filePath));\n            string slot = groupSlot.ToString(CultureInfo.InvariantCulture);\n            JsonObject group = root[slot] as JsonObject ?? AppGroupConfigSchema.CreateGroup(groupName);\n\n            string existingId = group["id"]?.GetValue<string>() ?? string.Empty;\n            if (!string.IsNullOrWhiteSpace(existingId) &&\n                !string.IsNullOrWhiteSpace(stableGroupId) &&\n                !string.Equals(existingId, stableGroupId, StringComparison.OrdinalIgnoreCase))\n            {\n                throw new InvalidOperationException($"Group slot {groupSlot} already belongs to stable ID '{existingId}'.");\n            }\n\n            string identity = !string.IsNullOrWhiteSpace(existingId)\n                ? existingId\n                : !string.IsNullOrWhiteSpace(stableGroupId)\n                    ? stableGroupId\n                    : AppGroupConfigSchema.NewStableId();\n\n            group["id"] = identity;\n            group["groupName"] = groupName;\n            group["groupHeader"] = groupHeader;\n            group["groupCol"] = groupCol;\n            group["groupIcon"] = groupIcon;\n            group["showLabels"] = showLabels;\n            group["labelSize"] = labelSize;\n            group["labelPosition"] = labelPosition;\n            group["headerPosition"] = headerPosition;\n            group["layout"] = layout;\n            group["showOnTray"] = showOnTray;\n            group["sortMode"] = sortMode;\n\n            Dictionary<string, JsonObject> existingItems = AppGroupConfigSchema.GetItems(group)\n                .OfType<JsonObject>()\n                .Where(item => !string.IsNullOrWhiteSpace(item["id"]?.GetValue<string>()))\n                .ToDictionary(item => item["id"]!.GetValue<string>(), item => (JsonObject)item.DeepClone(), StringComparer.OrdinalIgnoreCase);\n\n            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);\n            JsonArray canonicalItems = new();\n            foreach (PersistedGroupItem persisted in items)\n            {\n                string itemId = string.IsNullOrWhiteSpace(persisted.Id) ? AppGroupConfigSchema.NewStableId() : persisted.Id;\n                if (!seen.Add(itemId))\n                {\n                    throw new InvalidOperationException($"Duplicate item ID '{itemId}' in group '{identity}'.");\n                }\n\n                JsonObject item = existingItems.TryGetValue(itemId, out JsonObject? existingItem)\n                    ? existingItem\n                    : AppGroupConfigSchema.CreateItem(persisted.Target);\n                item["id"] = itemId;\n                item["target"] = persisted.Target;\n                item["displayName"] = persisted.DisplayName;\n                item["tooltip"] = persisted.DisplayName;\n                item["arguments"] = persisted.Arguments;\n                item["args"] = persisted.Arguments;\n                item["workingDirectory"] = persisted.WorkingDirectory;\n                item["icon"] = persisted.Icon;\n                item["runAsAdministrator"] = persisted.RunAsAdministrator;\n                item["appIdentity"] = persisted.AppIdentity;\n\n                string? subgroupId = persisted.SubgroupId;\n                if (string.IsNullOrWhiteSpace(subgroupId))\n                {\n                    subgroupId = ResolveSubgroupIdForTarget(root, persisted.Target);\n                }\n                string type = !string.IsNullOrWhiteSpace(subgroupId) ? "subgroup" : persisted.Type;\n                item["type"] = string.IsNullOrWhiteSpace(type) ? "launch" : type;\n                item["subgroupId"] = subgroupId;\n                canonicalItems.Add(item);\n            }\n\n            group["items"] = canonicalItems;\n            AppGroupConfigSchema.RebuildCompatibilityPath(group);\n            root[slot] = group;\n            root[AppGroupConfigSchema.VersionProperty] = AppGroupConfigSchema.CurrentVersion;\n            WriteAtomically(filePath, root.ToJsonString(IndentedJson));\n        }\n\n'''
text = replace_once(text, save_anchor, save_method + save_anchor, "canonical save method")
write(path, text)

# CLI compile imports.
path = "AppGroup/Program.cs"
text = read(path)
if "using System.Collections.Generic;" not in text:
    text = text.replace("using System;\n", "using System;\nusing System.Collections.Generic;\n", 1)
if "using System.Linq;" not in text:
    text = text.replace("using System.IO;\n", "using System.IO;\nusing System.Linq;\n", 1)
write(path, text)

# App only needs to forward the already-resolved stable selector to the existing popup.
path = "AppGroup/App.xaml.cs"
text = read(path)
old = 'NativeMethods.SendString(popupHWnd, $"{command}|{Program.InitialClickPos.X},{Program.InitialClickPos.Y}");'
new = 'string groupSelector = Program.InitialStableGroupId ?? command;\n                            NativeMethods.SendString(popupHWnd, $"{groupSelector}|{Program.InitialClickPos.X},{Program.InitialClickPos.Y}");'
text = replace_once(text, old, new, "App stable popup selector")
write(path, text)

# Main window keeps numeric slots strictly for ordering and preserves schema metadata when reordering.
path = "AppGroup/MainWindow.xaml.cs"
text = read(path)
text = replace_once(
    text,
    '                var newJsonObject = new JsonObject();',
    '                var newJsonObject = new JsonObject { [AppGroupConfigSchema.VersionProperty] = AppGroupConfigSchema.CurrentVersion };',
    "main reorder schema",
)
text = replace_once(
    text,
    '                if (!File.Exists(jsonFilePath))\n                    File.WriteAllText(jsonFilePath, "{}");\n\n                string jsonContent = await File.ReadAllTextAsync(jsonFilePath, cts.Token)',
    '                JsonConfigHelper.EnsureCurrentSchema(jsonFilePath);\n\n                string jsonContent = await File.ReadAllTextAsync(jsonFilePath, cts.Token)',
    "main ensure current schema",
)
write(path, text)

# Editor: enrich item view-model, load canonical items including broken/duplicate targets, and save stable artifacts.
path = "AppGroup/EditGroupWindow.xaml.cs"
text = read(path)
text = replace_once(
    text,
    '''        public class ExeFileModel {\n            public string FileName { get; set; }\n            public string FilePath { get; set; }\n            public string Icon { get; set; }\n            public string Tooltip { get; set; }\n            public string Args { get; set; }\n            public string IconPath { get; set; }\n        }''',
    '''        public class ExeFileModel {\n            public string? ItemId { get; set; }\n            public string FileName { get; set; } = string.Empty;\n            public string FilePath { get; set; } = string.Empty;\n            public string? Icon { get; set; }\n            public string Tooltip { get; set; } = string.Empty;\n            public string Args { get; set; } = string.Empty;\n            public string? IconPath { get; set; }\n            public string WorkingDirectory { get; set; } = string.Empty;\n            public bool RunAsAdministrator { get; set; }\n            public string Type { get; set; } = "launch";\n            public string? SubgroupId { get; set; }\n            public string? AppIdentity { get; set; }\n        }''',
    "editor model",
)
text = text.replace('            private string? groupName;\n', '            private string? groupName;\n            private string _stableGroupId = string.Empty;\n', 1)

# Read through the migration-aware helper rather than bypassing it.
text = replace_once(
    text,
    '                        string jsonContent = await File.ReadAllTextAsync(jsonFilePath);\n                        JsonNode jsonObject = JsonNode.Parse(jsonContent) ?? new JsonObject();',
    '                        string jsonContent = await JsonConfigHelper.ReadJsonFromFileAsync(jsonFilePath);\n                        JsonNode jsonObject = JsonNode.Parse(jsonContent) ?? new JsonObject();',
    "editor migration-aware read",
)
text = replace_once(
    text,
    '                            string gName = groupNode["groupName"]?.GetValue<string>();',
    '                            string gName = groupNode["groupName"]?.GetValue<string>() ?? string.Empty;\n                            string stableGroupId = groupNode["id"]?.GetValue<string>() ?? string.Empty;',
    "editor group stable id read",
)
text = replace_once(
    text,
    '                            JsonObject paths = groupNode["path"]?.AsObject();',
    '                            JsonArray items = groupNode["items"]?.AsArray() ?? new JsonArray();',
    "editor canonical items",
)
text = replace_once(
    text,
    '                                groupName = gName;',
    '                                groupName = gName;\n                                _stableGroupId = stableGroupId;',
    "editor keep stable id",
)
text = text.replace('                            if (groupCol > 0 && paths != null) {', '                            if (groupCol > 0) {', 1)
text = text.replace('                                    for (int i = 1; i <= paths.Count; i++)', '                                    for (int i = 1; i <= items.Count; i++)', 1)
text = replace_once(
    text,
    '''                                    ApplicationCount.Text = paths != null\n                                        ? paths.Count > 1 ? paths.Count + " Items"\n                                            : paths.Count == 1 ? "1 Item" : ""\n                                        : "";''',
    '''                                    ApplicationCount.Text = items.Count > 1 ? items.Count + " Items"\n                                        : items.Count == 1 ? "1 Item" : "";''',
    "editor canonical count",
)

legacy_items_pattern = r'''                            if \(paths != null\) \{\n                                foreach \(var path in paths\) \{.*?                                \}\n                            \}\n'''
canonical_items = '''                            foreach (JsonNode? itemNode in items) {\n                                if (itemNode is not JsonObject item) continue;\n                                string filePath = item["target"]?.GetValue<string>() ?? string.Empty;\n                                string itemId = item["id"]?.GetValue<string>() ?? string.Empty;\n                                string displayName = item["displayName"]?.GetValue<string>()\n                                    ?? item["tooltip"]?.GetValue<string>()\n                                    ?? Path.GetFileName(filePath);\n                                string arguments = item["arguments"]?.GetValue<string>()\n                                    ?? item["args"]?.GetValue<string>()\n                                    ?? string.Empty;\n                                string? icon = item["icon"]?.GetValue<string>();\n                                bool targetExists = !string.IsNullOrWhiteSpace(filePath) &&\n                                    (File.Exists(filePath) || Directory.Exists(filePath));\n                                if ((string.IsNullOrWhiteSpace(icon) || !File.Exists(icon)) && targetExists) {\n                                    icon = filePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)\n                                        ? await IconHelper.GetUrlFileIconAsync(filePath)\n                                        : await IconCache.GetIconPathAsync(filePath);\n                                }\n\n                                await Task.Delay(10);\n                                DispatcherQueue.TryEnqueue(() => {\n                                    ExeFiles.Add(new ExeFileModel {\n                                        ItemId = itemId,\n                                        FileName = string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(filePath) : displayName,\n                                        Icon = icon,\n                                        FilePath = filePath,\n                                        Tooltip = displayName,\n                                        Args = arguments,\n                                        IconPath = icon,\n                                        WorkingDirectory = item["workingDirectory"]?.GetValue<string>() ?? string.Empty,\n                                        RunAsAdministrator = item["runAsAdministrator"]?.GetValue<bool>() ?? false,\n                                        Type = item["type"]?.GetValue<string>() ?? "launch",\n                                        SubgroupId = item["subgroupId"]?.GetValue<string>(),\n                                        AppIdentity = item["appIdentity"]?.GetValue<string>()\n                                    });\n                                });\n                            }\n\n                            DispatcherQueue.TryEnqueue(() => {\n                                IconGridComboBox.Items.Clear();\n                                IconGridComboBox.Items.Add("2");\n                                if (ExeFiles.Count >= 9) IconGridComboBox.Items.Add("3");\n                                IconGridComboBox.SelectedItem = "2";\n                                if (!string.IsNullOrEmpty(groupIcon) && groupIcon.Contains("grid")) {\n                                    IconGridComboBox.SelectedItem = groupIcon.Contains("grid3") ? "3" : "2";\n                                    regularIcon = false;\n                                    IconGridComboBox.Visibility = Visibility.Visible;\n                                }\n                                ApplyExeListDisplay();\n                            });\n'''
text = regex_once(text, legacy_items_pattern, canonical_items, "editor canonical load")
text = replace_once(text, '                    groupName = "";', '                    groupName = "";\n                    _stableGroupId = AppGroupConfigSchema.NewStableId();', "editor new identity")

# Stable per-group artifact folder for item custom icons.
text = replace_once(
    text,
    '''                string groupsFolder = Path.Combine(AppPaths.BaseDataPath, "Groups");\n                Directory.CreateDirectory(groupsFolder);\n                string gName = GroupNameTextBox.Text?.Trim();\n                    string groupFolder = Path.Combine(groupsFolder, gName);\n                    currentGroupPath = Path.Combine(groupFolder, gName);''',
    '''                if (string.IsNullOrWhiteSpace(_stableGroupId))\n                    _stableGroupId = AppGroupConfigSchema.NewStableId();\n                string groupFolder = JsonConfigHelper.GetGroupFolderPath(_stableGroupId);\n                Directory.CreateDirectory(groupFolder);\n                currentGroupPath = Path.Combine(groupFolder, "Assets");''',
    "editor item icon folder",
)

create_method_pattern = r'''            private async void CreateShortcut_Click\(object sender, RoutedEventArgs e\) \{.*?            private string GetOldGroupName\(\) => groupName \?\? "";'''
create_method = '''            private async void CreateShortcut_Click(object sender, RoutedEventArgs e) {\n                var button = sender as Button;\n                if (button != null && !button.IsEnabled) return;\n                if (button != null) button.IsEnabled = false;\n\n                try {\n                    string newGroupName = GroupNameTextBox.Text?.Trim() ?? string.Empty;\n                    if (string.IsNullOrWhiteSpace(newGroupName)) {\n                        await ShowDialog("Error", "Please enter a group name.");\n                        return;\n                    }\n                    if (string.IsNullOrWhiteSpace(selectedIconPath)) {\n                        await ShowDialog("Error", "Please select an icon.");\n                        return;\n                    }\n                    if (string.IsNullOrWhiteSpace(_stableGroupId))\n                        _stableGroupId = AppGroupConfigSchema.NewStableId();\n\n                    string headerPosition = (HeaderPositionComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Top";\n                    string layout = (LayoutComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Default";\n                    string sortMode = (SortModeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DEFAULT_SORT_MODE;\n                    string groupFolder = JsonConfigHelper.GetGroupFolderPath(_stableGroupId);\n                    string assetFolder = Path.Combine(groupFolder, "Assets");\n                    Directory.CreateDirectory(assetFolder);\n                    File.SetAttributes(assetFolder, File.GetAttributes(assetFolder) | FileAttributes.Hidden);\n\n                    string iconBaseName = regularIcon ? "group_regular"\n                        : IconGridComboBox.SelectedItem?.ToString() == "3" ? "group_grid3" : "group_grid";\n                    string icoFilePath = Path.Combine(assetFolder, iconBaseName + ".ico");\n                    string originalImageExtension = Path.GetExtension(selectedIconPath);\n                    if (originalImageExtension.Equals(".ico", StringComparison.OrdinalIgnoreCase)) {\n                        File.Copy(selectedIconPath, icoFilePath, true);\n                    }\n                    else if (originalImageExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) {\n                        string extractedPngPath = await IconCache.GetIconPathAsync(selectedIconPath);\n                        if (string.IsNullOrEmpty(extractedPngPath)) {\n                            await ShowDialog("Error", "Failed to extract icon from EXE file.");\n                            return;\n                        }\n                        string pngFilePath = Path.Combine(assetFolder, iconBaseName + ".png");\n                        File.Copy(extractedPngPath, pngFilePath, true);\n                        if (!await IconHelper.ConvertToIco(pngFilePath, icoFilePath)) {\n                            await ShowDialog("Error", "Failed to convert extracted PNG to ICO format.");\n                            return;\n                        }\n                    }\n                    else if (!await IconHelper.ConvertToIco(selectedIconPath, icoFilePath)) {\n                        await ShowDialog("Error", "Failed to convert image to ICO format.");\n                        return;\n                    }\n\n                    if (!originalImageExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) {\n                        copiedImagePath = Path.Combine(assetFolder, iconBaseName + originalImageExtension);\n                        if (!string.Equals(Path.GetFullPath(selectedIconPath), Path.GetFullPath(copiedImagePath), StringComparison.OrdinalIgnoreCase))\n                            File.Copy(selectedIconPath, copiedImagePath, true);\n                    }\n\n                    string oldGroupName = GetOldGroupName();\n                    JsonConfigHelper.CreateOrUpdateGroupShortcut(_stableGroupId, newGroupName, icoFilePath);\n                    bool isPinned = await TaskbarManager.IsShortcutPinnedToTaskbar(_stableGroupId, oldGroupName);\n                    if (isPinned) {\n                        await TaskbarManager.UpdateTaskbarShortcutIcon(_stableGroupId, oldGroupName, newGroupName, icoFilePath);\n                        TaskbarManager.TryRefreshTaskbarWithoutRestartAsync();\n                    }\n\n                    bool groupHeader = GroupHeader.IsEnabled && GroupHeader.IsOn;\n                    if (GroupColComboBox.SelectedItem == null ||\n                        !int.TryParse(GroupColComboBox.SelectedItem.ToString(), out int groupCol) || groupCol <= 0) {\n                        await ShowDialog("Error", "Please select a valid group column value.");\n                        return;\n                    }\n\n                    List<PersistedGroupItem> persistedItems = ExeFiles.Select(item => new PersistedGroupItem(\n                        item.ItemId, item.FilePath, item.Tooltip, item.Args, item.IconPath ?? item.Icon ?? string.Empty,\n                        item.WorkingDirectory, item.RunAsAdministrator, item.Type, item.SubgroupId, item.AppIdentity)).ToList();\n\n                    bool showLabels = ShowLabels.IsOn;\n                    int labelSize = LabelSizeComboBox.SelectedItem != null\n                        ? int.Parse(LabelSizeComboBox.SelectedItem.ToString()) : DEFAULT_LABEL_SIZE;\n                    string labelPosition = LabelPositionComboBox.SelectedItem?.ToString() ?? DEFAULT_LABEL_POSITION;\n                    JsonConfigHelper.SaveGroupToJson(\n                        JsonConfigHelper.GetDefaultConfigPath(), GroupId, _stableGroupId, newGroupName,\n                        groupHeader, icoFilePath, groupCol, showLabels, labelSize, labelPosition, headerPosition,\n                        layout, ShowOnTray.IsOn, sortMode, persistedItems);\n                    GroupTrayManager.SyncFromJson();\n\n                    if (!string.IsNullOrEmpty(tempIcon) && File.Exists(tempIcon)) {\n                        try { File.Delete(tempIcon); } catch (Exception ex) { Debug.WriteLine(ex.Message); }\n                        tempIcon = null;\n                    }\n\n                    IntPtr hWnd = NativeMethods.FindWindow(null, "App Group");\n                    if (hWnd != IntPtr.Zero) NativeMethods.SetForegroundWindow(hWnd);\n                    GroupId = -1;\n                    Hide();\n                    _wasHidden = true;\n                }\n                catch (Exception ex) {\n                    await ShowDialog("Error", $"An error occurred: {ex.Message}");\n                }\n                finally {\n                    if (button != null) button.IsEnabled = true;\n                }\n            }\n\n            private string GetOldGroupName() => groupName ?? "";'''
text = regex_once(text, create_method_pattern, create_method, "editor stable save")
write(path, text)

# Popup: stable group selector and canonical items while leaving placement/animation untouched.
path = "AppGroup/PopupWindow.xaml.cs"
text = read(path)
text = replace_once(
    text,
    '''    public class PathData {\n        public string Tooltip { get; set; }\n        public string Args { get; set; }\n        public string Icon { get; set; }\n    }''',
    '''    public class PathData {\n        public string? Id { get; set; }\n        public string Target { get; set; } = string.Empty;\n        public string DisplayName { get; set; } = string.Empty;\n        public string Arguments { get; set; } = string.Empty;\n        public string WorkingDirectory { get; set; } = string.Empty;\n        public string Icon { get; set; } = string.Empty;\n        public bool RunAsAdministrator { get; set; }\n        public string Type { get; set; } = "launch";\n        public string? SubgroupId { get; set; }\n        public string? AppIdentity { get; set; }\n        public string Tooltip { get; set; } = string.Empty;\n        public string Args { get; set; } = string.Empty;\n    }''',
    "popup path model",
)
text = text.replace('        public required string GroupIcon { get; set; }\n        public required string GroupName { get; set; }', '        public string Id { get; set; } = string.Empty;\n        public required string GroupIcon { get; set; }\n        public required string GroupName { get; set; }', 1)
text = text.replace('        public Dictionary<string, PathData> Path { get; set; }\n', '        public Dictionary<string, PathData> Path { get; set; } = new();\n        public List<PathData> Items { get; set; } = new();\n', 1)
text = text.replace('        public string Path { get; set; }\n        public string Name { get; set; }', '        public string? ItemId { get; set; }\n        public string Path { get; set; }\n        public string Name { get; set; }', 1)
text = text.replace('        public bool IsSubgroup { get; set; }\n        public string SubgroupName { get; set; }', '        public bool IsSubgroup { get; set; }\n        public string SubgroupName { get; set; }\n        public string WorkingDirectory { get; set; } = string.Empty;\n        public bool RunAsAdministrator { get; set; }\n        public string Type { get; set; } = "launch";\n        public string? SubgroupId { get; set; }\n        public string? AppIdentity { get; set; }', 1)

constructor_anchor = '        internal async Task PreloadSubgroupsAsync(CancellationToken token = default) {'
helpers = '''        private bool GroupMatches(GroupData group, string selector) {\n            if (string.Equals(group.Id, selector, StringComparison.OrdinalIgnoreCase)) return true;\n            if (!string.Equals(group.GroupName, selector, StringComparison.OrdinalIgnoreCase)) return false;\n            return _groups?.Values.Count(candidate =>\n                string.Equals(candidate.GroupName, selector, StringComparison.OrdinalIgnoreCase)) == 1;\n        }\n\n        private KeyValuePair<string, GroupData> FindGroupBySelector(string selector) {\n            if (_groups == null || string.IsNullOrWhiteSpace(selector)) return default;\n            return _groups.FirstOrDefault(pair => GroupMatches(pair.Value, selector));\n        }\n\n        private static int GetItemCount(GroupData group) =>\n            group.Items?.Count > 0 ? group.Items.Count : group.Path?.Count ?? 0;\n\n        private static IReadOnlyList<PathData> GetCanonicalItems(GroupData group) {\n            if (group.Items?.Count > 0) return group.Items;\n            return group.Path.Select(pair => {\n                PathData item = pair.Value;\n                item.Target = pair.Key;\n                item.DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Tooltip : item.DisplayName;\n                item.Arguments = string.IsNullOrWhiteSpace(item.Arguments) ? item.Args : item.Arguments;\n                return item;\n            }).ToList();\n        }\n\n'''
text = replace_once(text, constructor_anchor, helpers + constructor_anchor, "popup selector helpers")

# Stable selector matches throughout identity lookups.
text = text.replace('g.Value.GroupName.Equals(_groupFilter, StringComparison.OrdinalIgnoreCase)', 'GroupMatches(g.Value, _groupFilter)')
text = text.replace('g.GroupName.Equals(_groupFilter, StringComparison.OrdinalIgnoreCase)', 'GroupMatches(g, _groupFilter)')
text = text.replace('g.Value.GroupName.Equals(subgroupName, StringComparison.OrdinalIgnoreCase)', 'GroupMatches(g.Value, subgroupName)')

# Ignore schema metadata during legacy Dictionary deserialization.
text = text.replace('return JsonSerializer.Deserialize<Dictionary<string, GroupData>>(json, JsonOptions);', 'return JsonSerializer.Deserialize<Dictionary<string, GroupData>>(JsonConfigHelper.GetGroupsOnlyJson(json), JsonOptions);')
text = text.replace('_groups = JsonSerializer.Deserialize<Dictionary<string, GroupData>>(_json, JsonOptions);', '_groups = JsonSerializer.Deserialize<Dictionary<string, GroupData>>(JsonConfigHelper.GetGroupsOnlyJson(_json), JsonOptions);')

text = text.replace('maxPathItems = filteredGroup.Value.Path.Count;', 'maxPathItems = GetItemCount(filteredGroup.Value);')
text = text.replace('maxPathItems = Math.Max(maxPathItems, group.Path.Count);', 'maxPathItems = Math.Max(maxPathItems, GetItemCount(group));')
text = text.replace('await LoadGridItems(group.Value.Path);', 'await LoadGridItems(GetCanonicalItems(group.Value));')

# Subgroup preloading prefers persisted stable reference; COM description remains legacy fallback.
text = replace_once(
    text,
    '''                foreach (var pathEntry in match.Value.Path) {\n                    string path = pathEntry.Key;\n                    if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {''',
    '''                foreach (var pathEntry in match.Value.Path) {\n                    string path = pathEntry.Key;\n                    if (!string.IsNullOrWhiteSpace(pathEntry.Value.SubgroupId)) {\n                        subgroupNames.Add(pathEntry.Value.SubgroupId);\n                        continue;\n                    }\n                    if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {''',
    "popup subgroup stable preload",
)

load_grid_pattern = r'''        private async Task LoadGridItems\(Dictionary<string, PathData> pathsWithProperties\) \{.*?            var loadToken = _iconLoadCts.Token;'''
load_grid = '''        private async Task LoadGridItems(IReadOnlyList<PathData> persistedItems) {\n            var subgroupInfo = new Dictionary<string, (bool isSubgroup, string subgroupSelector)>();\n            foreach (PathData properties in persistedItems) {\n                string path = properties.Target;\n                if (!string.IsNullOrWhiteSpace(properties.SubgroupId)) {\n                    subgroupInfo[properties.Id ?? Guid.NewGuid().ToString("D")] = (true, properties.SubgroupId);\n                    continue;\n                }\n                if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) continue;\n                try {\n                    IWshShell shell = new WshShell();\n                    IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(path);\n                    string comment = shortcut.Description;\n                    if (!string.IsNullOrEmpty(comment) && comment.EndsWith("- AppGroup Shortcut", StringComparison.OrdinalIgnoreCase)) {\n                        subgroupInfo[properties.Id ?? path] = (true, comment.Replace("- AppGroup Shortcut", "").Trim());\n                    }\n                }\n                catch (Exception ex) {\n                    Debug.WriteLine($"Failed to read shortcut comment: {ex.Message}");\n                }\n            }\n\n            var items = await Task.Run(() => {\n                var result = new List<PopupItem>();\n                foreach (PathData properties in persistedItems) {\n                    string path = properties.Target;\n                    string tooltip = !string.IsNullOrEmpty(properties.DisplayName)\n                        ? properties.DisplayName\n                        : !string.IsNullOrEmpty(properties.Tooltip) ? properties.Tooltip : GetDisplayNameBackground(path);\n                    string customIconPath = !string.IsNullOrEmpty(properties.Icon) ? properties.Icon : null;\n                    string itemKey = properties.Id ?? path;\n                    var popupItem = new PopupItem {\n                        ItemId = properties.Id,\n                        Path = path,\n                        Name = Path.GetFileNameWithoutExtension(path),\n                        ToolTip = tooltip,\n                        Icon = null,\n                        Args = !string.IsNullOrEmpty(properties.Arguments) ? properties.Arguments : properties.Args ?? string.Empty,\n                        IconPath = customIconPath,\n                        CustomIconPath = customIconPath,\n                        WorkingDirectory = properties.WorkingDirectory ?? string.Empty,\n                        RunAsAdministrator = properties.RunAsAdministrator,\n                        Type = properties.Type ?? "launch",\n                        SubgroupId = properties.SubgroupId,\n                        AppIdentity = properties.AppIdentity\n                    };\n                    if (subgroupInfo.TryGetValue(itemKey, out var info)) {\n                        popupItem.IsSubgroup = info.isSubgroup;\n                        popupItem.SubgroupName = info.subgroupSelector;\n                    }\n                    result.Add(popupItem);\n                }\n                if (_sortMode == "Alphabetical")\n                    result = result.OrderBy(i => i.ToolTip, StringComparer.OrdinalIgnoreCase).ToList();\n                return result;\n            });\n\n            var loadToken = _iconLoadCts.Token;'''
text = regex_once(text, load_grid_pattern, load_grid, "popup canonical grid")

# Stable subgroup selector: deleted child gets a clear recovery path.
open_pattern = r'''        private async void OpenSubPopup\(string groupName\) \{\n            var clickPos = _lastClickPos;'''
open_replacement = '''        private async void OpenSubPopup(string groupName) {\n            if (_groups == null || FindGroupBySelector(groupName).Key == null) {\n                ShowErrorDialog("The referenced subgroup no longer exists. Edit this group to repair or remove the subgroup item.");\n                return;\n            }\n            var clickPos = _lastClickPos;'''
text = regex_once(text, open_pattern, open_replacement, "popup deleted subgroup")

# Stable artifact path and pin lookup.
update_shortcut_pattern = r'''                string localAppDataPath = Environment.GetFolderPath\(Environment.SpecialFolder.LocalApplicationData\);\n                string groupsFolder = Path.Combine\(localAppDataPath, "AppGroup", "Groups"\);\n                string groupFolder = Path.Combine\(groupsFolder, _groupFilter\);\n                if \(!Directory.Exists\(groupFolder\)\) return;\n\n                string shortcutPath = Path.Combine\(groupFolder, \$"\{_groupFilter\}\\.lnk"\);'''
# The interpolated extension is easier and safer to replace literally below.
old_shortcut = '''                string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);\n                string groupsFolder = Path.Combine(localAppDataPath, "AppGroup", "Groups");\n                string groupFolder = Path.Combine(groupsFolder, _groupFilter);\n                if (!Directory.Exists(groupFolder)) return;\n\n                string shortcutPath = Path.Combine(groupFolder, $"{_groupFilter}.lnk");'''
text = replace_once(text, old_shortcut, '                string shortcutPath = JsonConfigHelper.GetGroupShortcutPath(_groupFilter);', "popup stable shortcut path")
text = text.replace('bool isPinned = await TaskbarManager.IsShortcutPinnedToTaskbar(_groupFilter);', 'bool isPinned = await TaskbarManager.IsShortcutPinnedToTaskbar(_groupFilter);')

# Canonical duplicate-safe popup save helper inserted before old JSON updater.
json_update_anchor = '        private async Task UpdateJsonConfiguration(string newIconPath, int gridSize) {'
save_popup = '''        private void SavePopupGroup(KeyValuePair<string, GroupData> filteredGroup, string iconPath) {\n            if (!int.TryParse(filteredGroup.Key, out int groupId)) return;\n            List<PersistedGroupItem> persisted = PopupItems.Select(item => new PersistedGroupItem(\n                item.ItemId, item.Path, item.ToolTip, item.Args, item.CustomIconPath ?? string.Empty,\n                item.WorkingDirectory, item.RunAsAdministrator, item.Type, item.SubgroupId, item.AppIdentity)).ToList();\n            JsonConfigHelper.SaveGroupToJson(\n                JsonConfigHelper.GetDefaultConfigPath(), groupId, filteredGroup.Value.Id,\n                filteredGroup.Value.GroupName, filteredGroup.Value.GroupHeader, iconPath, filteredGroup.Value.GroupCol,\n                filteredGroup.Value.ShowLabels, filteredGroup.Value.LabelSize > 0 ? filteredGroup.Value.LabelSize : DEFAULT_LABEL_SIZE,\n                filteredGroup.Value.LabelPosition ?? DEFAULT_LABEL_POSITION, filteredGroup.Value.HeaderPosition ?? "Top",\n                filteredGroup.Value.Layout ?? "Default", filteredGroup.Value.ShowOnTray, filteredGroup.Value.SortMode ?? "Manual", persisted);\n        }\n\n'''
text = replace_once(text, json_update_anchor, save_popup + json_update_anchor, "popup save helper")

update_json_pattern = r'''        private async Task UpdateJsonConfiguration\(string newIconPath, int gridSize\) \{.*?        private async void GridView_DragItemsCompleted'''
update_json_replacement = '''        private async Task UpdateJsonConfiguration(string newIconPath, int gridSize) {\n            try {\n                var filteredGroup = FindGroupBySelector(_groupFilter);\n                if (filteredGroup.Key == null) return;\n                SavePopupGroup(filteredGroup, newIconPath);\n                string configPath = JsonConfigHelper.GetDefaultConfigPath();\n                _json = JsonConfigHelper.ReadJsonFromFile(configPath);\n                _groups = JsonSerializer.Deserialize<Dictionary<string, GroupData>>(JsonConfigHelper.GetGroupsOnlyJson(_json), JsonOptions);\n                await Task.CompletedTask;\n            }\n            catch (Exception ex) {\n                Debug.WriteLine($"Error updating JSON configuration: {ex.Message}");\n            }\n        }\n\n        private async void GridView_DragItemsCompleted'''
text = regex_once(text, update_json_pattern, update_json_replacement, "popup canonical config save")

drag_pattern = r'''        private async void GridView_DragItemsCompleted\(ListViewBase sender, DragItemsCompletedEventArgs args\) \{.*?        private string GetDisplayNameBackground'''
drag_replacement = '''        private async void GridView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) {\n            try {\n                if (_groups == null || string.IsNullOrEmpty(_groupFilter)) return;\n                var filteredGroup = FindGroupBySelector(_groupFilter);\n                if (filteredGroup.Key == null) return;\n                string currentIcon = filteredGroup.Value.GroupIcon;\n                if (currentIcon.Contains("grid"))\n                    await CreateGridIconFromReorder();\n                else\n                    SavePopupGroup(filteredGroup, currentIcon);\n                _json = JsonConfigHelper.ReadJsonFromFile(JsonConfigHelper.GetDefaultConfigPath());\n            }\n            catch (Exception ex) {\n                Debug.WriteLine($"Error in GridView_DragItemsCompleted: {ex.Message}");\n                ShowErrorDialog($"Failed to save new item order: {ex.Message}");\n            }\n        }\n\n        private string GetDisplayNameBackground'''
text = regex_once(text, drag_pattern, drag_replacement, "popup canonical reorder")

# Specific remaining name lookups become stable selector lookups.
text = text.replace('var filteredGroup = _groups.FirstOrDefault(g =>\n                    GroupMatches(g.Value, _groupFilter));', 'var filteredGroup = FindGroupBySelector(_groupFilter);')
text = text.replace('var matchingGroup = _groups?.Values.FirstOrDefault(g =>\n                    GroupMatches(g, _groupFilter));\n                if (matchingGroup != null)\n                    await JsonConfigHelper.LaunchAll(matchingGroup.GroupName);', 'var matchingGroup = FindGroupBySelector(_groupFilter);\n                if (matchingGroup.Key != null)\n                    await JsonConfigHelper.LaunchAll(matchingGroup.Value.Id);')
write(path, text)

print("Stable identity bridge patches applied.")
