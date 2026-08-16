namespace EgressGeo;

public sealed record UserTimerState(
    bool IsAvailable,
    bool IsEnabled,
    bool IsActive);
