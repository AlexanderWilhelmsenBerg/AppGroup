using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AppGroup;

public static class AppGroupConfigSchema
{
    public const int CurrentVersion = 2;
    public const string VersionProperty = "$schemaVersion";

    public static MigrationResult Migrate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            json = "{}";
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ConfigMigrationException("The AppGroup configuration is not valid JSON. The original file was not changed.", ex);
        }

        if (parsed is not JsonObject root)
        {
            throw new ConfigMigrationException("The AppGroup configuration root must be a JSON object. The original file was not changed.");
        }

        int version = ReadVersion(root);
        if (version > CurrentVersion)
        {
            throw new ConfigMigrationException($"Configuration schema {version} is newer than this AppGroup build supports ({CurrentVersion}).");
        }

        JsonObject migrated = (JsonObject)root.DeepClone();
        bool changed = false;

        if (version < 2)
        {
            MigrateLegacyToV2(migrated);
            migrated[VersionProperty] = CurrentVersion;
            changed = true;
        }
        else
        {
            ValidateV2(migrated);
        }

        string result = migrated.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return new MigrationResult(result, changed, CurrentVersion);
    }

    public static JsonObject ParseCurrent(string json)
    {
        MigrationResult migration = Migrate(json);
        return JsonNode.Parse(migration.Json)?.AsObject()
            ?? throw new ConfigMigrationException("Unable to parse the migrated AppGroup configuration.");
    }

    public static IEnumerable<KeyValuePair<string, JsonObject>> EnumerateGroups(JsonObject root)
    {
        foreach ((string key, JsonNode? value) in root)
        {
            if (key == VersionProperty || !int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            if (value is JsonObject group)
            {
                yield return new KeyValuePair<string, JsonObject>(key, group);
            }
        }
    }

    public static JsonObject CreateEmpty()
    {
        return new JsonObject { [VersionProperty] = CurrentVersion };
    }

    public static string NewStableId() => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);

    public static string CreateDeterministicLegacyId(string kind, params string?[] parts)
    {
        string seed = "AppGroup/stable-identity/v2/" + kind + "/" + string.Join("/", parts.Select(part => part ?? string.Empty));
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> bytes = stackalloc byte[16];
        digest.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString("D", CultureInfo.InvariantCulture);
    }

    public static JsonObject CreateGroup(string displayName)
    {
        return new JsonObject
        {
            ["id"] = NewStableId(),
            ["groupName"] = displayName,
            ["groupHeader"] = false,
            ["groupCol"] = 1,
            ["groupIcon"] = string.Empty,
            ["showLabels"] = false,
            ["labelSize"] = 12,
            ["labelPosition"] = "Bottom",
            ["headerPosition"] = "Top",
            ["layout"] = "Default",
            ["showOnTray"] = false,
            ["sortMode"] = "Manual",
            ["items"] = new JsonArray(),
            ["path"] = new JsonObject()
        };
    }

    public static JsonObject CreateItem(
        string target,
        string? displayName = null,
        string? arguments = null,
        string? workingDirectory = null,
        string? icon = null,
        bool runAsAdministrator = false,
        string? type = null,
        string? subgroupId = null,
        string? appIdentity = null)
    {
        return new JsonObject
        {
            ["id"] = NewStableId(),
            ["target"] = target,
            ["displayName"] = displayName ?? string.Empty,
            ["arguments"] = arguments ?? string.Empty,
            ["workingDirectory"] = workingDirectory ?? string.Empty,
            ["icon"] = icon ?? string.Empty,
            ["runAsAdministrator"] = runAsAdministrator,
            ["type"] = type ?? (string.IsNullOrWhiteSpace(subgroupId) ? "launch" : "subgroup"),
            ["subgroupId"] = subgroupId,
            ["appIdentity"] = appIdentity
        };
    }

    public static JsonArray GetItems(JsonObject group)
    {
        if (group["items"] is JsonArray items)
        {
            return items;
        }

        JsonArray created = new();
        group["items"] = created;
        return created;
    }

    public static GroupResolution ResolveGroup(JsonObject root, string selector, bool allowLegacyName = true)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return GroupResolution.NotFound(selector);
        }

        List<GroupReference> groups = EnumerateGroups(root)
            .Select(pair => new GroupReference(
                pair.Key,
                pair.Value["id"]?.GetValue<string>() ?? string.Empty,
                pair.Value["groupName"]?.GetValue<string>() ?? string.Empty))
            .ToList();

        GroupReference? idMatch = groups.FirstOrDefault(group =>
            string.Equals(group.StableId, selector, StringComparison.OrdinalIgnoreCase));
        if (idMatch is not null)
        {
            return GroupResolution.Found(idMatch);
        }

        if (!allowLegacyName)
        {
            return GroupResolution.NotFound(selector);
        }

        List<GroupReference> nameMatches = groups
            .Where(group => string.Equals(group.DisplayName, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return nameMatches.Count switch
        {
            1 => GroupResolution.Found(nameMatches[0]),
            > 1 => GroupResolution.Ambiguous(selector, nameMatches),
            _ => GroupResolution.NotFound(selector)
        };
    }

    public static GroupReference? FindGroupByStableId(JsonObject root, string stableId)
    {
        GroupResolution resolution = ResolveGroup(root, stableId, allowLegacyName: false);
        return resolution.Status == GroupResolutionStatus.Found ? resolution.Group : null;
    }

    public static void RenameGroup(JsonObject root, string stableId, string newDisplayName)
    {
        GroupReference group = FindGroupByStableId(root, stableId)
            ?? throw new KeyNotFoundException($"No group exists with stable ID '{stableId}'.");
        root[group.Slot]!["groupName"] = newDisplayName;
    }

    public static bool DeleteGroup(JsonObject root, string stableId)
    {
        GroupReference? group = FindGroupByStableId(root, stableId);
        return group is not null && root.Remove(group.Slot);
    }

    public static string AddGroup(JsonObject root, JsonObject group)
    {
        string stableId = group["id"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(stableId))
        {
            stableId = NewStableId();
            group["id"] = stableId;
        }

        if (FindGroupByStableId(root, stableId) is not null)
        {
            throw new InvalidOperationException($"A group with stable ID '{stableId}' already exists.");
        }

        int next = EnumerateGroups(root)
            .Select(pair => int.Parse(pair.Key, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max() + 1;
        root[next.ToString(CultureInfo.InvariantCulture)] = group.DeepClone();
        root[VersionProperty] = CurrentVersion;
        return stableId;
    }

    public static string AddItem(JsonObject group, JsonObject item)
    {
        string stableId = item["id"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(stableId))
        {
            stableId = NewStableId();
            item["id"] = stableId;
        }

        JsonArray items = GetItems(group);
        if (items.OfType<JsonObject>().Any(existing =>
                string.Equals(existing["id"]?.GetValue<string>(), stableId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"An item with stable ID '{stableId}' already exists in the group.");
        }

        items.Add(item.DeepClone());
        RebuildCompatibilityPath(group);
        return stableId;
    }

    public static void UpdateItem(JsonObject group, string itemId, Action<JsonObject> update)
    {
        JsonObject item = GetItems(group)
            .OfType<JsonObject>()
            .FirstOrDefault(candidate => string.Equals(candidate["id"]?.GetValue<string>(), itemId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"No item exists with stable ID '{itemId}'.");

        update(item);
        item["id"] = itemId;
        RebuildCompatibilityPath(group);
    }

    public static void RebuildCompatibilityPath(JsonObject group)
    {
        JsonObject path = new();
        foreach (JsonObject item in GetItems(group).OfType<JsonObject>())
        {
            string? target = item["target"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(target) || path.ContainsKey(target))
            {
                continue;
            }

            path[target] = new JsonObject
            {
                ["tooltip"] = item["displayName"]?.GetValue<string>() ?? string.Empty,
                ["args"] = item["arguments"]?.GetValue<string>() ?? string.Empty,
                ["icon"] = item["icon"]?.GetValue<string>() ?? string.Empty,
                ["itemId"] = item["id"]?.GetValue<string>() ?? string.Empty,
                ["workingDirectory"] = item["workingDirectory"]?.GetValue<string>() ?? string.Empty,
                ["runAsAdministrator"] = item["runAsAdministrator"]?.GetValue<bool>() ?? false,
                ["type"] = item["type"]?.GetValue<string>() ?? "launch",
                ["subgroupId"] = item["subgroupId"]?.GetValue<string>(),
                ["appIdentity"] = item["appIdentity"]?.GetValue<string>()
            };
        }

        group["path"] = path;
    }

    public static ConfigMergeResult MergeForImport(string existingJson, string importedJson, bool replaceConflictingIds)
    {
        JsonObject existing = ParseCurrent(existingJson);
        JsonObject imported = ParseCurrent(importedJson);
        JsonObject merged = (JsonObject)existing.DeepClone();

        List<string> conflicts = new();
        List<string> alreadyPresent = new();
        List<string> added = new();
        List<string> replaced = new();

        foreach ((_, JsonObject importedGroup) in EnumerateGroups(imported))
        {
            string importedId = importedGroup["id"]?.GetValue<string>()
                ?? throw new ConfigMigrationException("Imported group is missing a stable ID after migration.");
            GroupReference? existingReference = FindGroupByStableId(merged, importedId);

            if (existingReference is null)
            {
                AddGroup(merged, importedGroup);
                added.Add(importedId);
                continue;
            }

            JsonObject existingGroup = merged[existingReference.Slot]!.AsObject();
            if (JsonNode.DeepEquals(existingGroup, importedGroup))
            {
                alreadyPresent.Add(importedId);
                continue;
            }

            if (!replaceConflictingIds)
            {
                conflicts.Add(importedId);
                continue;
            }

            merged[existingReference.Slot] = importedGroup.DeepClone();
            replaced.Add(importedId);
        }

        merged[VersionProperty] = CurrentVersion;
        return new ConfigMergeResult(
            merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            added,
            alreadyPresent,
            replaced,
            conflicts);
    }

    private static int ReadVersion(JsonObject root)
    {
        JsonNode? versionNode = root[VersionProperty];
        if (versionNode is null)
        {
            return 1;
        }

        if (versionNode is JsonValue && versionNode.TryGetValue<int>(out int version) && version >= 1)
        {
            return version;
        }

        throw new ConfigMigrationException($"'{VersionProperty}' must be a positive integer.");
    }

    private static void MigrateLegacyToV2(JsonObject root)
    {
        List<KeyValuePair<string, JsonObject>> groups = EnumerateGroups(root).ToList();
        Dictionary<string, List<string>> idsByName = new(StringComparer.OrdinalIgnoreCase);

        foreach ((string slot, JsonObject group) in groups)
        {
            string name = group["groupName"]?.GetValue<string>() ?? string.Empty;
            string groupId = group["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(groupId))
            {
                groupId = CreateDeterministicLegacyId("group", slot, name);
                group["id"] = groupId;
            }

            if (!idsByName.TryGetValue(name, out List<string>? ids))
            {
                ids = new List<string>();
                idsByName[name] = ids;
            }
            ids.Add(groupId);
        }

        foreach ((string slot, JsonObject group) in groups)
        {
            string groupId = group["id"]?.GetValue<string>()
                ?? throw new ConfigMigrationException($"Group slot '{slot}' could not be assigned a stable ID.");
            JsonArray items = new();

            if (group["path"] is JsonObject legacyPaths)
            {
                int ordinal = 0;
                foreach ((string target, JsonNode? detailsNode) in legacyPaths)
                {
                    JsonObject item = detailsNode is JsonObject details
                        ? (JsonObject)details.DeepClone()
                        : new JsonObject { ["legacyValue"] = detailsNode?.DeepClone() };

                    string args = item["args"]?.GetValue<string>() ?? string.Empty;
                    string tooltip = item["tooltip"]?.GetValue<string>() ?? string.Empty;
                    string itemId = CreateDeterministicLegacyId("item", groupId, ordinal.ToString(CultureInfo.InvariantCulture), target, args, tooltip);
                    item["id"] = itemId;
                    item["target"] = target;
                    item["displayName"] ??= tooltip;
                    item["arguments"] ??= args;
                    item["workingDirectory"] ??= string.Empty;
                    item["runAsAdministrator"] ??= false;
                    item["type"] ??= "launch";
                    item["appIdentity"] ??= null;
                    item["subgroupId"] ??= null;
                    items.Add(item);
                    ordinal++;
                }
            }

            group["items"] = items;
        }

        foreach ((_, JsonObject group) in groups)
        {
            foreach (JsonObject item in GetItems(group).OfType<JsonObject>())
            {
                if (!string.IsNullOrWhiteSpace(item["subgroupId"]?.GetValue<string>()))
                {
                    item["type"] = "subgroup";
                    continue;
                }

                string? target = item["target"]?.GetValue<string>();
                string? candidateName = TryGetLegacySubgroupName(target);
                if (candidateName is null || !idsByName.TryGetValue(candidateName, out List<string>? candidates) || candidates.Count != 1)
                {
                    continue;
                }

                item["type"] = "subgroup";
                item["subgroupId"] = candidates[0];
            }

            RebuildCompatibilityPath(group);
        }

        ValidateV2(root);
    }

    private static string? TryGetLegacySubgroupName(string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string normalized = target.Replace('/', '\\');
        string fileName = System.IO.Path.GetFileNameWithoutExtension(normalized);
        string? parent = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(normalized));
        if (string.IsNullOrWhiteSpace(fileName) || !string.Equals(fileName, parent, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fileName;
    }

    private static void ValidateV2(JsonObject root)
    {
        HashSet<string> groupIds = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string slot, JsonObject group) in EnumerateGroups(root))
        {
            string id = group["id"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ConfigMigrationException($"Group slot '{slot}' is missing its stable ID.");
            }
            if (!groupIds.Add(id))
            {
                throw new ConfigMigrationException($"Duplicate stable group ID '{id}' was found. The configuration was not rewritten.");
            }

            if (group["items"] is not JsonArray items)
            {
                throw new ConfigMigrationException($"Group '{id}' is missing its canonical items collection.");
            }

            HashSet<string> itemIds = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode? itemNode in items)
            {
                if (itemNode is not JsonObject item)
                {
                    throw new ConfigMigrationException($"Group '{id}' contains an invalid item entry.");
                }

                string itemId = item["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(itemId) || !itemIds.Add(itemId))
                {
                    throw new ConfigMigrationException($"Group '{id}' contains a missing or duplicate item ID '{itemId}'.");
                }
            }
        }
    }
}

public sealed record MigrationResult(string Json, bool Changed, int SchemaVersion);

public sealed class ConfigMigrationException : Exception
{
    public ConfigMigrationException(string message) : base(message) { }
    public ConfigMigrationException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed record GroupReference(string Slot, string StableId, string DisplayName);

public enum GroupResolutionStatus
{
    Found,
    NotFound,
    Ambiguous
}

public sealed record GroupResolution(
    GroupResolutionStatus Status,
    string Selector,
    GroupReference? Group,
    IReadOnlyList<GroupReference> Matches)
{
    public static GroupResolution Found(GroupReference group) =>
        new(GroupResolutionStatus.Found, group.StableId, group, new[] { group });

    public static GroupResolution NotFound(string selector) =>
        new(GroupResolutionStatus.NotFound, selector, null, Array.Empty<GroupReference>());

    public static GroupResolution Ambiguous(string selector, IReadOnlyList<GroupReference> matches) =>
        new(GroupResolutionStatus.Ambiguous, selector, null, matches);
}

public sealed record ConfigMergeResult(
    string Json,
    IReadOnlyList<string> AddedIds,
    IReadOnlyList<string> AlreadyPresentIds,
    IReadOnlyList<string> ReplacedIds,
    IReadOnlyList<string> ConflictIds);
