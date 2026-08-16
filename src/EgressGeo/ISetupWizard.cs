namespace EgressGeo;

public interface ISetupWizard
{
    ValueTask<int> Run(CancellationToken cancellationToken);
}
