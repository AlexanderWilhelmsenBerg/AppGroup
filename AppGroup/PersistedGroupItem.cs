namespace AppGroup;

public sealed record PersistedGroupItem(
    string? Id,
    string Target,
    string DisplayName,
    string Arguments,
    string Icon,
    string WorkingDirectory,
    bool RunAsAdministrator,
    string Type,
    string? SubgroupId,
    string? AppIdentity);
