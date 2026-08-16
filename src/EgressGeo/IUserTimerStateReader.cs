namespace EgressGeo;

public interface IUserTimerStateReader
{
    ValueTask<UserTimerState> Read(
        CancellationToken cancellationToken);
}
