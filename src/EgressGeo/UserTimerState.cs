namespace EgressGeo;

public abstract record UserTimerState
{
    private UserTimerState()
    {
    }

    public sealed record Unavailable : UserTimerState;

    public sealed record Available(bool IsEnabled, bool IsActive) :
        UserTimerState;
}
