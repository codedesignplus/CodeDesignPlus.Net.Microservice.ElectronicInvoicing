using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Core.Mapster;

namespace CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Test.Core.Mapster;

/// <summary>
/// Every <c>MapWith(src => new XDto { … })</c> assigns every property of the DTO.
/// </summary>
/// <remarks>
/// A handwritten mapping silently forgets the field added later to the DTO: it keeps its default value and nothing
/// fails. The test reads the expression Mapster stored for each rule and compares it with the properties of the
/// destination, so a new mapping enters by itself. Those built with a constructor (<c>new XCommand(…)</c>) are not
/// needed: there the compiler already demands every argument.
/// </remarks>
public class MapWithCoverageTest
{
    // DTO properties a mapping leaves unassigned on purpose, with the reason. Empty: there are none today.
    private static readonly Dictionary<(Type Destination, string Property), string> Unmapped = [];

    [Fact]
    public void Configure_EveryMapWith_AssignsEveryDestinationProperty()
    {
        MapsterConfig.Configure();

        var missing = new List<string>();
        var checkedRules = 0;

        foreach (var (types, rule) in TypeAdapterConfig.GlobalSettings.RuleMap)
        {
            if (rule.Settings.ConverterFactory?.Invoke(null!) is not LambdaExpression { Body: MemberInitExpression body })
                continue;

            checkedRules++;

            var assigned = body.Bindings.Select(binding => binding.Member.Name).ToHashSet();

            missing.AddRange(types.Destination.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.SetMethod is { IsPublic: true })
                .Where(property => !assigned.Contains(property.Name))
                .Where(property => !Unmapped.ContainsKey((types.Destination, property.Name)))
                .Select(property => $"{types.Source.Name} -> {types.Destination.Name}.{property.Name}"));
        }

        Assert.True(checkedRules > 0, "No MapWith with an object initializer was found: the test is not looking where it should.");
        Assert.Empty(missing);
    }
}
