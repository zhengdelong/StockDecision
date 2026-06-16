namespace StockDecision.Infrastructure;

public sealed class InfrastructureAssemblyMarker
{
    public static Type ApplicationMarkerType => typeof(Application.ApplicationAssemblyMarker);

    public static Type DomainMarkerType => typeof(Domain.DomainAssemblyMarker);

    private InfrastructureAssemblyMarker()
    {
    }
}
