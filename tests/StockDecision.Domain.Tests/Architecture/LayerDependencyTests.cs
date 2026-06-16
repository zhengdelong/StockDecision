using StockDecision.Application;
using StockDecision.Domain;
using StockDecision.Infrastructure;

namespace StockDecision.Domain.Tests.Architecture;

public class LayerDependencyTests
{
    [Fact]
    public void Domain_Should_Not_Depend_On_Application_Or_Infrastructure()
    {
        var referencedAssemblies = typeof(DomainAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("StockDecision.Application", referencedAssemblies);
        Assert.DoesNotContain("StockDecision.Infrastructure", referencedAssemblies);
    }

    [Fact]
    public void Application_Should_Depend_On_Domain_But_Not_Infrastructure()
    {
        var referencedAssemblies = typeof(ApplicationAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("StockDecision.Domain", referencedAssemblies);
        Assert.DoesNotContain("StockDecision.Infrastructure", referencedAssemblies);
    }

    [Fact]
    public void Infrastructure_Should_Depend_On_Application_And_Domain()
    {
        var referencedAssemblies = typeof(InfrastructureAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("StockDecision.Application", referencedAssemblies);
        Assert.Contains("StockDecision.Domain", referencedAssemblies);
    }
}
