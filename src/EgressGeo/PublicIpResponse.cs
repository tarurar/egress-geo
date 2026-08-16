namespace EgressGeo;

public abstract record PublicIpResponse
{
    private PublicIpResponse()
    {
    }

    public sealed record Received(string Content) : PublicIpResponse;

    public sealed record Unavailable : PublicIpResponse;
}
