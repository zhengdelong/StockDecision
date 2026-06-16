namespace StockDecision.Application;

public sealed class ApplicationAssemblyMarker
{
    public static Type DomainMarkerType => typeof(Domain.DomainAssemblyMarker);

    private ApplicationAssemblyMarker()
    {
    }
}
