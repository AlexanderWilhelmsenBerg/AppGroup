using IWshRuntimeLibrary;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using File = System.IO.File;

namespace AppGroup
{
    public class JsonConfigHelper
    {
        private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

        public static string GetDefaultConfigPath(string fileName = "appgroups.json")
        {
            string appDataPath = AppPaths.BaseDataPath;
            Directory.CreateDirectory(appDataPath);
            return Path.Combine(appDataPath, fileName);
        }

        public static void EnsureCurrentSchema()
        {
            EnsureCurrentSchema(GetDefaultConfigPath());
        }

        public static void EnsureCurrentSchema(string filePath)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(filePath))
            {
                WriteAtomically(filePath, AppGroupConfigSchema.CreateEmpty().ToJsonString(IndentedJson));
                return;
            }

            string original = File.ReadAllText(filePath);
            MigrationResult migration = AppGroupConfigSchema.Migrate(original);
            if (!migration.Changed)
            {
                return;
            }

            string backupPath = filePath + ".pre-schema2.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(filePath, backupPath, overwrite: false);
            }

            WriteAtomically(filePath, migration.Json);
        }

        public static string ReadJsonFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"JSON configuration file not found at: {filePath}");
                }

                if (IsDefaultConfigPath(filePath))
                {
                    EnsureCurrentSchema(filePath);
                }
                return File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is not ConfigMigrationException)
            {
                throw new Exception($"Error reading JSON file: {ex.Message}", ex);
            }
        }

        public static async Task<string> ReadJsonFromFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"JSON configuration file not found at: {filePath}");
                }

                if (IsDefaultConfigPath(filePath))
                {
                    EnsureCurrentSchema(filePath);
                }
                return await File.ReadAllTextAsync(filePath);
            }
            catch (Exception ex) when (ex is not ConfigMigrationException)
            {
                throw new Exception($"Error reading JSON file: {ex.Message}", ex);
            }
        }

        public static JsonObject ReadCurrentRoot()
        {
            EnsureCurrentSchema();
            return AppGroupConfigSchema.ParseCurrent(File.ReadAllText(GetDefaultConfigPath()));
        }

        public static int GetNextGroupId()
        {
            JsonObject root = ReadCurrentRoot();
            return AppGroupConfigSchema.EnumerateGroups(root)
                .Select(pair => int.Parse(pair.Key, CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        public static string FindGroupNameByKey(int key)
        {
            JsonObject root = ReadCurrentRoot();
            if (root[key.ToString(CultureInfo.InvariantCulture)] is JsonObject group)
            {
                return group["groupName"]?.GetValue<string>() ?? string.Empty;
            }

            return string.Empty;
        }

        public static string FindStableIdByKey(int key)
        {
            JsonObject root = ReadCurrentRoot();
            if (root[key.ToString(CultureInfo.InvariantCulture)] is JsonObject group)
            {
                return group["id"]?.GetValue<string>() ?? string.Empty;
            }

            return string.Empty;
        }

        public static int FindKeyByStableGroupId(string stableGroupId)
        {
            JsonObject root = ReadCurrentRoot();
            GroupReference reference = AppGroupConfigSchema.FindGroupByStableId(root, stableGroupId)
                ?? throw new KeyNotFoundException($"No group found for stable ID '{stableGroupId}'.");
            return int.Parse(reference.Slot, CultureInfo.InvariantCulture);
        }

        public static int FindKeyByGroupName(string groupName)
        {
            JsonObject root = ReadCurrentRoot();
            GroupResolution resolution = AppGroupConfigSchema.ResolveGroup(root, groupName, allowLegacyName: true);
            return resolution.Status switch
            {
                GroupResolutionStatus.Found => int.Parse(resolution.Group!.Slot, CultureInfo.InvariantCulture),
                GroupResolutionStatus.Ambiguous => throw new AmbiguousMatchException(
                    $"More than one group is named '{groupName}'. Use a stable group ID instead."),
                _ => throw new KeyNotFoundException($"No group found for groupName '{groupName}'.")
            };
        }

        public static GroupResolution ResolveGroup(string selector, bool allowLegacyName = true)
        {
            return AppGroupConfigSchema.ResolveGroup(ReadCurrentRoot(), selector, allowLegacyName);
        }

        public static string ResolveStableGroupId(string selector, bool allowLegacyName = true)
        {
            GroupResolution resolution = ResolveGroup(selector, allowLegacyName);
            return resolution.Status switch
            {
                GroupResolutionStatus.Found => resolution.Group!.StableId,
                GroupResolutionStatus.Ambiguous => throw new AmbiguousMatchException(
                    $"More than one group is named '{selector}'. Use a stable group ID instead."),
                _ => throw new KeyNotFoundException($"No group found for '{selector}'.")
            };
        }

        public static string GetGroupFolderPath(string stableGroupId)
        {
            return Path.Combine(AppPaths.BaseDataPath, "Groups", stableGroupId);
        }

        public static string GetGroupShortcutPath(string stableGroupId)
        {
            return Path.Combine(GetGroupFolderPath(stableGroupId), "AppGroup.lnk");
        }

        public static string BuildGroupActivationArguments(string stableGroupId)
        {
            return $"--group \"{stableGroupId}\"";
        }

        public static string BuildEditActivationArguments(string stableGroupId)
        {
            return $"EditGroupWindow --group \"{stableGroupId}\"";
        }

        public static string BuildLaunchAllArguments(string stableGroupId)
        {
            return $"LaunchAll --group \"{stableGroupId}\"";
        }

        public static void CreateOrUpdateGroupShortcut(string stableGroupId, string groupName, string iconPath)
        {
            string shortcutPath = GetGroupShortcutPath(stableGroupId);
            string? directory = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string targetPath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "AppGroup.exe");

            WshShell shell = new();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.Arguments = BuildGroupActivationArguments(stableGroupId);
            shortcut.Description = $"{stableGroupId} - AppGroup Shortcut";
            shortcut.IconLocation = iconPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.Save();
        }

        public static void AddGroupToJson(
            string filePath,
            int groupId,
            string groupName,
            bool groupHeader,
            string groupIcon,
            int groupCol,
            bool showLabels,
            int labelSize,
            string labelPosition,
            string headerPosition,
            string layout,
            bool showOnTray,
            string sortMode,
            Dictionary<string, (string tooltip, string args, string icon)> paths)
        {
            try
            {
                EnsureCurrentSchema(filePath);
                JsonObject root = AppGroupConfigSchema.ParseCurrent(File.ReadAllText(filePath));
                string slot = groupId.ToString(CultureInfo.InvariantCulture);
                JsonObject group = root[slot] as JsonObject ?? AppGroupConfigSchema.CreateGroup(groupName);

                group["groupName"] = groupName;
                group["groupHeader"] = groupHeader;
                group["groupCol"] = groupCol;
                group["groupIcon"] = groupIcon;
                group["showLabels"] = showLabels;
                group["labelSize"] = labelSize;
                group["labelPosition"] = labelPosition;
                group["headerPosition"] = headerPosition;
                group["layout"] = layout;
                group["showOnTray"] = showOnTray;
                group["sortMode"] = sortMode;

                SynchronizeItemsFromLegacyUi(root, group, paths);
                root[slot] = group;
                root[AppGroupConfigSchema.VersionProperty] = AppGroupConfigSchema.CurrentVersion;
                WriteAtomically(filePath, root.ToJsonString(IndentedJson));
            }
            catch (Exception ex)
            {
                throw new Exception($"Error adding group to JSON file: {ex.Message}", ex);
            }
        }

        public static void DeleteGroupFromJson(string filePath, int groupId)
        {
            try
            {
                EnsureCurrentSchema(filePath);
                JsonObject root = AppGroupConfigSchema.ParseCurrent(File.ReadAllText(filePath));
                string slot = groupId.ToString(CultureInfo.InvariantCulture);
                JsonObject group = root[slot] as JsonObject
                    ?? throw new KeyNotFoundException($"Group slot {groupId} not found in JSON file.");
                string stableId = group["id"]?.GetValue<string>() ?? string.Empty;

                root.Remove(slot);
                WriteAtomically(filePath, root.ToJsonString(IndentedJson));

                if (!string.IsNullOrWhiteSpace(stableId))
                {
                    string stableFolder = GetGroupFolderPath(stableId);
                    if (Directory.Exists(stableFolder))
                    {
                        Directory.Delete(stableFolder, recursive: true);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error deleting group: {ex.Message}", ex);
            }
        }

        public static void DuplicateGroupInJson(string filePath, int groupId)
        {
            try
            {
                EnsureCurrentSchema(filePath);
                JsonObject root = AppGroupConfigSchema.ParseCurrent(File.ReadAllText(filePath));
                string slot = groupId.ToString(CultureInfo.InvariantCulture);
                JsonObject source = root[slot] as JsonObject
                    ?? throw new KeyNotFoundException($"Group slot {groupId} not found in JSON file.");
                JsonObject duplicate = (JsonObject)source.DeepClone();

                string sourceName = source["groupName"]?.GetValue<string>() ?? "Group";
                duplicate["groupName"] = sourceName + " - Copy";
                duplicate["id"] = AppGroupConfigSchema.NewStableId();
                if (duplicate["items"] is JsonArray items)
                {
                    foreach (JsonObject item in items.OfType<JsonObject>())
                    {
                        item["id"] = AppGroupConfigSchema.NewStableId();
                    }
                }
                AppGroupConfigSchema.RebuildCompatibilityPath(duplicate);
                AppGroupConfigSchema.AddGroup(root, duplicate);
                WriteAtomically(filePath, root.ToJsonString(IndentedJson));
            }
            catch (Exception ex)
            {
                throw new Exception($"Error duplicating group in JSON file: {ex.Message}", ex);
            }
        }

        public static async Task LaunchAll(string groupSelector)
        {
            try
            {
                JsonObject root = ReadCurrentRoot();
                GroupResolution resolution = AppGroupConfigSchema.ResolveGroup(root, groupSelector, allowLegacyName: true);
                if (resolution.Status != GroupResolutionStatus.Found)
                {
                    Debug.WriteLine(resolution.Status == GroupResolutionStatus.Ambiguous
                        ? $"LaunchAll refused ambiguous legacy group name '{groupSelector}'."
                        : $"LaunchAll could not resolve group '{groupSelector}'.");
                    return;
                }

                JsonObject group = root[resolution.Group!.Slot]!.AsObject();
                JsonArray items = AppGroupConfigSchema.GetItems(group);
                List<Task> launches = new();
                foreach (JsonObject item in items.OfType<JsonObject>())
                {
                    if (string.Equals(item["type"]?.GetValue<string>(), "subgroup", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string target = item["target"]?.GetValue<string>() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(target))
                    {
                        continue;
                    }
                    string arguments = item["arguments"]?.GetValue<string>() ?? string.Empty;
                    launches.Add(Task.Run(() => LaunchLegacyCompatible(target, arguments)));
                }
                await Task.WhenAll(launches);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error launching all paths under group '{groupSelector}': {ex.Message}");
            }
        }

        public static bool GroupExistsInJson(string groupName)
        {
            JsonObject root = ReadCurrentRoot();
            return AppGroupConfigSchema.EnumerateGroups(root).Any(pair =>
                string.Equals(pair.Value["groupName"]?.GetValue<string>(), groupName, StringComparison.OrdinalIgnoreCase));
        }

        public static bool StableGroupIdExists(string stableGroupId)
        {
            return AppGroupConfigSchema.FindGroupByStableId(ReadCurrentRoot(), stableGroupId) is not null;
        }

        public static bool GroupIdExists(int groupId)
        {
            JsonObject root = ReadCurrentRoot();
            return root.ContainsKey(groupId.ToString(CultureInfo.InvariantCulture));
        }

        public static void OpenGroupFolder(int groupId)
        {
            try
            {
                JsonObject root = ReadCurrentRoot();
                string slot = groupId.ToString(CultureInfo.InvariantCulture);
                if (root[slot] is not JsonObject group)
                {
                    return;
                }

                string stableId = group["id"]?.GetValue<string>() ?? string.Empty;
                string groupName = group["groupName"]?.GetValue<string>() ?? string.Empty;
                string stableFolder = GetGroupFolderPath(stableId);
                string legacyFolder = Path.Combine(AppPaths.BaseDataPath, "Groups", groupName);
                string folder = Directory.Exists(stableFolder) ? stableFolder : legacyFolder;
                if (Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = folder,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening group folder: {ex.Message}");
            }
        }

        public static void UpdateShortcutIcon(string shortcutPath, string originalGroupName, string newGroupName)
        {
            try
            {
                WshShell shell = new();
                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);
                shortcut.IconLocation = shortcut.IconLocation.Replace(originalGroupName, newGroupName, StringComparison.OrdinalIgnoreCase);
                shortcut.Save();
            }
            catch (Exception ex)
            {
                throw new Exception($"Error updating shortcut icon: {ex.Message}", ex);
            }
        }

        private static void SynchronizeItemsFromLegacyUi(
            JsonObject root,
            JsonObject group,
            Dictionary<string, (string tooltip, string args, string icon)> paths)
        {
            JsonArray existingItems = AppGroupConfigSchema.GetItems(group);
            List<JsonObject> oldItems = existingItems.OfType<JsonObject>().Select(item => (JsonObject)item.DeepClone()).ToList();
            HashSet<string> consumedIds = new(StringComparer.OrdinalIgnoreCase);
            JsonArray updated = new();

            foreach ((string target, (string tooltip, string args, string icon) details) in paths)
            {
                JsonObject? item = oldItems.FirstOrDefault(candidate =>
                    !consumedIds.Contains(candidate["id"]?.GetValue<string>() ?? string.Empty) &&
                    string.Equals(candidate["target"]?.GetValue<string>(), target, StringComparison.OrdinalIgnoreCase));

                item ??= AppGroupConfigSchema.CreateItem(target);
                string itemId = item["id"]?.GetValue<string>() ?? AppGroupConfigSchema.NewStableId();
                item["id"] = itemId;
                item["target"] = target;
                item["displayName"] = details.tooltip ?? string.Empty;
                item["arguments"] = details.args ?? string.Empty;
                item["icon"] = details.icon ?? string.Empty;
                item["workingDirectory"] ??= string.Empty;
                item["runAsAdministrator"] ??= false;
                item["appIdentity"] ??= null;

                string? subgroupId = ResolveSubgroupIdForTarget(root, target);
                if (!string.IsNullOrWhiteSpace(subgroupId))
                {
                    item["type"] = "subgroup";
                    item["subgroupId"] = subgroupId;
                }
                else
                {
                    item["type"] ??= "launch";
                    item["subgroupId"] ??= null;
                }

                consumedIds.Add(itemId);
                updated.Add(item);
            }

            HashSet<string> uiTargets = paths.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (JsonObject oldItem in oldItems)
            {
                string id = oldItem["id"]?.GetValue<string>() ?? string.Empty;
                if (consumedIds.Contains(id))
                {
                    continue;
                }

                string target = oldItem["target"]?.GetValue<string>() ?? string.Empty;
                bool duplicateTarget = uiTargets.Contains(target);
                bool targetMissing = !string.IsNullOrWhiteSpace(target) && !File.Exists(target) && !Directory.Exists(target);
                if (duplicateTarget || targetMissing || string.Equals(oldItem["type"]?.GetValue<string>(), "subgroup", StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(oldItem);
                }
            }

            group["items"] = updated;
            AppGroupConfigSchema.RebuildCompatibilityPath(group);
        }

        private static string? ResolveSubgroupIdForTarget(JsonObject root, string target)
        {
            foreach ((_, JsonObject candidate) in AppGroupConfigSchema.EnumerateGroups(root))
            {
                string id = candidate["id"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(id) && PathsEqual(GetGroupShortcutPath(id), target))
                {
                    return id;
                }
            }

            if (!target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string candidateName = Path.GetFileNameWithoutExtension(target);
            GroupResolution legacy = AppGroupConfigSchema.ResolveGroup(root, candidateName, allowLegacyName: true);
            return legacy.Status == GroupResolutionStatus.Found ? legacy.Group!.StableId : null;
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void LaunchLegacyCompatible(string target, string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{target}\" {arguments}",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo)?.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch {target}: {ex.Message}");
            }
        }

        private static bool IsDefaultConfigPath(string path)
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(GetDefaultConfigPath()), StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteAtomically(string filePath, string content)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = filePath + ".tmp." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            try
            {
                File.WriteAllText(tempPath, content);
                if (File.Exists(filePath))
                {
                    File.Move(tempPath, filePath, overwrite: true);
                }
                else
                {
                    File.Move(tempPath, filePath);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
