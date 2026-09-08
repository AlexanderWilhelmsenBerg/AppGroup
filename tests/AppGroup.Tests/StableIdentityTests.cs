using System.Text.Json.Nodes;
using Xunit;

namespace AppGroup.Tests;

public sealed class StableIdentityTests
{
    private static string LoadLegacyFixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-v1-appgroups.json"));

    [Fact]
    public void LegacyFixture_MigratesToSchema2_WithStableGroupAndItemIds()
    {
        MigrationResult result = AppGroupConfigSchema.Migrate(LoadLegacyFixture());
        JsonObject root = JsonNode.Parse(result.Json)!.AsObject();

        Assert.True(result.Changed);
        Assert.Equal(2, root[AppGroupConfigSchema.VersionProperty]!.GetValue<int>());
        Assert.Equal(3, AppGroupConfigSchema.EnumerateGroups(root).Count());

        foreach ((_, JsonObject group) in AppGroupConfigSchema.EnumerateGroups(root))
        {
            Assert.False(string.IsNullOrWhiteSpace(group["id"]!.GetValue<string>()));
            JsonArray items = group["items"]!.AsArray();
            Assert.All(items.OfType<JsonObject>(), item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item["id"]!.GetValue<string>()));
                Assert.NotNull(item["target"]);
            });
        }
    }

    [Fact]
    public void LegacyMigration_IsDeterministic()
    {
        string source = LoadLegacyFixture();
        MigrationResult first = AppGroupConfigSchema.Migrate(source);
        MigrationResult secondIndependent = AppGroupConfigSchema.Migrate(source);
        Assert.Equal(first.Json, secondIndependent.Json);
    }

    [Fact]
    public void Schema2Migration_IsIdempotent()
    {
        MigrationResult first = AppGroupConfigSchema.Migrate(LoadLegacyFixture());
        MigrationResult second = AppGroupConfigSchema.Migrate(first.Json);

        Assert.False(second.Changed);
        Assert.Equal(first.Json, second.Json);
    }

    [Fact]
    public void LegacyMigration_PreservesUnknownAndMissingTargetData()
    {
        JsonObject root = AppGroupConfigSchema.ParseCurrent(LoadLegacyFixture());
        JsonObject work = AppGroupConfigSchema.EnumerateGroups(root).First().Value;

        Assert.Equal("preserve-me", work["futureGroupSetting"]!.GetValue<string>());
        JsonObject example = work["items"]!.AsArray().OfType<JsonObject>()
            .Single(item => item["target"]!.GetValue<string>().EndsWith("example.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(42, example["futureItemSetting"]!.GetValue<int>());

        JsonObject missing = work["items"]!.AsArray().OfType<JsonObject>()
            .Single(item => item["target"]!.GetValue<string>().Contains("Missing", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Missing but retained", missing["displayName"]!.GetValue<string>());
    }

    [Fact]
    public void InvalidJson_FailsWithoutProducingReplacementData()
    {
        Assert.Throws<ConfigMigrationException>(() => AppGroupConfigSchema.Migrate("{ definitely-not-json"));
    }

    [Fact]
    public void Rename_PreservesGroupIdentity()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        JsonObject group = AppGroupConfigSchema.CreateGroup("Original");
        string id = AppGroupConfigSchema.AddGroup(root, group);

        AppGroupConfigSchema.RenameGroup(root, id, "Renamed");

        GroupReference resolved = AppGroupConfigSchema.FindGroupByStableId(root, id)!;
        Assert.Equal("Renamed", resolved.DisplayName);
        Assert.Equal(id, root[resolved.Slot]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void DuplicateGroupNames_RemainIndependent()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        string first = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Same"));
        string second = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Same"));

        Assert.NotEqual(first, second);
        Assert.Equal(GroupResolutionStatus.Ambiguous, AppGroupConfigSchema.ResolveGroup(root, "Same").Status);
        Assert.Equal(first, AppGroupConfigSchema.ResolveGroup(root, first).Group!.StableId);
        Assert.Equal(second, AppGroupConfigSchema.ResolveGroup(root, second).Group!.StableId);
    }

    [Fact]
    public void LegacyNameActivation_WorksOnlyWhenUnique()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        string id = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Unique"));
        Assert.Equal(id, AppGroupConfigSchema.ResolveGroup(root, "Unique").Group!.StableId);

        AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Unique"));
        GroupResolution ambiguous = AppGroupConfigSchema.ResolveGroup(root, "Unique");
        Assert.Equal(GroupResolutionStatus.Ambiguous, ambiguous.Status);
        Assert.Null(ambiguous.Group);
    }

    [Fact]
    public void DeleteAndRecreate_DoesNotReuseIdentity()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        string oldId = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Group"));
        Assert.True(AppGroupConfigSchema.DeleteGroup(root, oldId));
        string newId = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Group"));
        Assert.NotEqual(oldId, newId);
    }

    [Fact]
    public void DuplicateTargets_HaveIndependentItemIdentities()
    {
        JsonObject group = AppGroupConfigSchema.CreateGroup("Apps");
        string first = AppGroupConfigSchema.AddItem(group, AppGroupConfigSchema.CreateItem("C:\\Tools\\same.exe", arguments: "--one"));
        string second = AppGroupConfigSchema.AddItem(group, AppGroupConfigSchema.CreateItem("C:\\Tools\\same.exe", arguments: "--two"));

        Assert.NotEqual(first, second);
        Assert.Equal(2, group["items"]!.AsArray().Count);
        Assert.Single(group["path"]!.AsObject());
    }

    [Fact]
    public void ItemEdits_DoNotChangeIdentity()
    {
        JsonObject group = AppGroupConfigSchema.CreateGroup("Apps");
        string id = AppGroupConfigSchema.AddItem(group, AppGroupConfigSchema.CreateItem("C:\\Tools\\app.exe", "Before", "--old"));

        AppGroupConfigSchema.UpdateItem(group, id, item =>
        {
            item["target"] = "C:\\Tools\\renamed.exe";
            item["displayName"] = "After";
            item["arguments"] = "--new";
            item["workingDirectory"] = "C:\\Work";
            item["runAsAdministrator"] = true;
        });

        JsonObject updated = group["items"]!.AsArray().OfType<JsonObject>().Single();
        Assert.Equal(id, updated["id"]!.GetValue<string>());
        Assert.Equal("C:\\Tools\\renamed.exe", updated["target"]!.GetValue<string>());
    }

    [Fact]
    public void MissingTarget_DoesNotAffectItemIdentity()
    {
        JsonObject group = AppGroupConfigSchema.CreateGroup("Broken");
        string id = AppGroupConfigSchema.AddItem(group, AppGroupConfigSchema.CreateItem("Z:\\not-there.exe"));
        Assert.Equal(id, group["items"]!.AsArray()[0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void MigratedSubgroupReference_SurvivesChildRename()
    {
        JsonObject root = AppGroupConfigSchema.ParseCurrent(LoadLegacyFixture());
        JsonObject child = AppGroupConfigSchema.EnumerateGroups(root).Single(pair => pair.Value["groupName"]!.GetValue<string>() == "Child").Value;
        string childId = child["id"]!.GetValue<string>();
        JsonObject parent = AppGroupConfigSchema.EnumerateGroups(root).Single(pair => pair.Value["groupName"]!.GetValue<string>() == "Parent").Value;
        JsonObject subgroup = parent["items"]!.AsArray().OfType<JsonObject>().Single();

        Assert.Equal(childId, subgroup["subgroupId"]!.GetValue<string>());
        AppGroupConfigSchema.RenameGroup(root, childId, "Child Renamed");
        Assert.Equal(childId, subgroup["subgroupId"]!.GetValue<string>());
    }

    [Fact]
    public void DuplicateSubgroupNames_AreSafeWithStableReferences()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        string child1 = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Child"));
        string child2 = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Child"));
        JsonObject parent = AppGroupConfigSchema.CreateGroup("Parent");
        AppGroupConfigSchema.AddItem(parent, AppGroupConfigSchema.CreateItem("", "Child 2", type: "subgroup", subgroupId: child2));
        AppGroupConfigSchema.AddGroup(root, parent);

        Assert.NotEqual(child1, child2);
        Assert.Equal(GroupResolutionStatus.Ambiguous, AppGroupConfigSchema.ResolveGroup(root, "Child").Status);
        Assert.NotNull(AppGroupConfigSchema.FindGroupByStableId(root, child2));
    }

    [Fact]
    public void DeletedSubgroup_LeavesRecoverableReference()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        string child = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Child"));
        JsonObject parent = AppGroupConfigSchema.CreateGroup("Parent");
        AppGroupConfigSchema.AddItem(parent, AppGroupConfigSchema.CreateItem("", type: "subgroup", subgroupId: child));
        string parentId = AppGroupConfigSchema.AddGroup(root, parent);

        Assert.True(AppGroupConfigSchema.DeleteGroup(root, child));
        JsonObject persistedParent = root[AppGroupConfigSchema.FindGroupByStableId(root, parentId)!.Slot]!.AsObject();
        Assert.Equal(child, persistedParent["items"]!.AsArray()[0]!["subgroupId"]!.GetValue<string>());
        Assert.Null(AppGroupConfigSchema.FindGroupByStableId(root, child));
    }

    [Fact]
    public void ExportImportRoundTrip_PreservesIdentity()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        JsonObject group = AppGroupConfigSchema.CreateGroup("Roundtrip");
        string itemId = AppGroupConfigSchema.AddItem(group, AppGroupConfigSchema.CreateItem("C:\\App.exe"));
        string groupId = AppGroupConfigSchema.AddGroup(root, group);
        string exported = root.ToJsonString();

        ConfigMergeResult result = AppGroupConfigSchema.MergeForImport(AppGroupConfigSchema.CreateEmpty().ToJsonString(), exported, false);
        JsonObject imported = AppGroupConfigSchema.ParseCurrent(result.Json);
        JsonObject importedGroup = imported[AppGroupConfigSchema.FindGroupByStableId(imported, groupId)!.Slot]!.AsObject();

        Assert.Equal(groupId, importedGroup["id"]!.GetValue<string>());
        Assert.Equal(itemId, importedGroup["items"]!.AsArray()[0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void OldFormatImport_IsMigrated()
    {
        ConfigMergeResult result = AppGroupConfigSchema.MergeForImport(AppGroupConfigSchema.CreateEmpty().ToJsonString(), LoadLegacyFixture(), false);
        JsonObject imported = AppGroupConfigSchema.ParseCurrent(result.Json);
        Assert.Equal(3, AppGroupConfigSchema.EnumerateGroups(imported).Count());
        Assert.All(AppGroupConfigSchema.EnumerateGroups(imported), pair => Assert.NotNull(pair.Value["id"]));
    }

    [Fact]
    public void ImportSameIdentitySameContent_IsNoOp()
    {
        JsonObject root = AppGroupConfigSchema.CreateEmpty();
        string id = AppGroupConfigSchema.AddGroup(root, AppGroupConfigSchema.CreateGroup("Same"));
        string json = root.ToJsonString();

        ConfigMergeResult result = AppGroupConfigSchema.MergeForImport(json, json, false);
        Assert.Contains(id, result.AlreadyPresentIds);
        Assert.Empty(result.ConflictIds);
        Assert.Empty(result.AddedIds);
    }

    [Fact]
    public void ImportSameIdentityDifferentContent_RequiresExplicitReplacement()
    {
        JsonObject existing = AppGroupConfigSchema.CreateEmpty();
        JsonObject group = AppGroupConfigSchema.CreateGroup("Before");
        string id = AppGroupConfigSchema.AddGroup(existing, group);
        JsonObject imported = (JsonObject)existing.DeepClone();
        AppGroupConfigSchema.RenameGroup(imported, id, "After");

        ConfigMergeResult rejected = AppGroupConfigSchema.MergeForImport(existing.ToJsonString(), imported.ToJsonString(), false);
        Assert.Contains(id, rejected.ConflictIds);
        Assert.Equal("Before", AppGroupConfigSchema.FindGroupByStableId(AppGroupConfigSchema.ParseCurrent(rejected.Json), id)!.DisplayName);

        ConfigMergeResult replaced = AppGroupConfigSchema.MergeForImport(existing.ToJsonString(), imported.ToJsonString(), true);
        Assert.Contains(id, replaced.ReplacedIds);
        Assert.Equal("After", AppGroupConfigSchema.FindGroupByStableId(AppGroupConfigSchema.ParseCurrent(replaced.Json), id)!.DisplayName);
    }

    [Fact]
    public void ImportDuplicateDisplayNames_DoesNotMergeThem()
    {
        JsonObject imported = AppGroupConfigSchema.CreateEmpty();
        string first = AppGroupConfigSchema.AddGroup(imported, AppGroupConfigSchema.CreateGroup("Duplicate"));
        string second = AppGroupConfigSchema.AddGroup(imported, AppGroupConfigSchema.CreateGroup("Duplicate"));

        ConfigMergeResult result = AppGroupConfigSchema.MergeForImport(AppGroupConfigSchema.CreateEmpty().ToJsonString(), imported.ToJsonString(), false);
        JsonObject merged = AppGroupConfigSchema.ParseCurrent(result.Json);
        Assert.NotNull(AppGroupConfigSchema.FindGroupByStableId(merged, first));
        Assert.NotNull(AppGroupConfigSchema.FindGroupByStableId(merged, second));
        Assert.Equal(GroupResolutionStatus.Ambiguous, AppGroupConfigSchema.ResolveGroup(merged, "Duplicate").Status);
    }
}
