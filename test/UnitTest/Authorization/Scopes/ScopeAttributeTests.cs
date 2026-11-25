using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Authorization.Scopes;
using Altinn.Platform.Storage.Controllers;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.Scopes;

public class ScopeAttributeTests
{
    /// <summary>
    /// Controllers that take the authorization service or the scope authorizer but whose endpoints need no
    /// API scope declaration.
    /// </summary>
    private static readonly Type[] _instanceControllerExceptions = [];

    /// <summary>
    /// The controllers whose endpoints concern instances, so each must declare its API scope requirement:
    /// every controller that takes the authorization service or the scope authorizer.
    /// </summary>
    private static readonly Type[] _instanceControllers =
    [
        .. ControllersTaking(typeof(IAuthorization), typeof(IInstanceScopeAuthorizer))
            .Except(_instanceControllerExceptions)
            .OrderBy(type => type.Name, StringComparer.Ordinal),
    ];

    /// <summary>
    /// Controllers that may load instances from the repository instead of through the authorization
    /// service: they are not called with end user or app tokens on behalf of a party.
    /// </summary>
    private static readonly Type[] _instanceRepositoryExceptions =
    [
        typeof(CleanupController),
        typeof(ContentOnDemandController),
        typeof(StudioInstancesController),
        typeof(MigrationController),
    ];

    /// <summary>
    /// Controllers that may search instances through the query repository, which does not authorize the
    /// instances it returns. Their search endpoints authorize the caller themselves: instance search filters
    /// the results, and messagebox search is limited to the sync adapter.
    /// </summary>
    private static readonly Type[] _instanceQueryRepositoryUsers =
    [
        typeof(InstancesController),
        typeof(MessageBoxInstancesController),
    ];

    public static TheoryData<string> InstanceActions()
    {
        TheoryData<string> data = new();
        foreach (MethodInfo action in GetInstanceActions())
        {
            data.Add($"{action.DeclaringType!.Name}.{action.Name}");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InstanceActions))]
    public void InstanceAction_DeclaresExactlyOneScopeRequirement(string actionName)
    {
        MethodInfo action = GetInstanceActions()
            .Single(a => $"{a.DeclaringType!.Name}.{a.Name}" == actionName);

        int declarations =
            action.GetCustomAttributes<InstanceScopeAccessAttribute>().Count()
            + action.GetCustomAttributes<RequireScopeAttribute>().Count()
            + action.GetCustomAttributes<ExcludeFromScopeCheckAttribute>().Count();

        Assert.True(
            declarations == 1,
            $"{actionName} must have exactly one of [InstanceScopeAccess], [RequireScope] and [ExcludeFromScopeCheck], but has {declarations}."
        );
    }

    [Fact]
    public async Task InstanceActions_ScopeRequirements()
    {
        SortedDictionary<string, string> requirements = new(StringComparer.Ordinal);
        foreach (MethodInfo action in GetInstanceActions())
        {
            requirements[$"{action.DeclaringType!.Name}.{action.Name}"] = Describe(action);
        }

        await VerifyXunit.Verifier.Verify(requirements);
    }

    [Fact]
    public void InstanceControllers_IncludeTheKnownInstanceControllers()
    {
        Assert.Contains(typeof(InstancesController), _instanceControllers);
        Assert.Contains(typeof(DataController), _instanceControllers);
        Assert.Contains(typeof(InstanceMutationsController), _instanceControllers);
        Assert.Contains(typeof(MessageBoxInstancesController), _instanceControllers);
    }

    [Fact]
    public void Controllers_DoNotTakeTheInstanceRepository()
    {
        Assert.Empty(
            ControllersTaking(typeof(IInstanceRepository))
                .Except(_instanceRepositoryExceptions)
                .Select(type => type.Name)
        );
    }

    [Fact]
    public void InstanceRepositoryExceptions_StillTakeTheInstanceRepository()
    {
        Assert.Empty(
            _instanceRepositoryExceptions
                .Except(ControllersTaking(typeof(IInstanceRepository)))
                .Select(type => type.Name)
        );
    }

    [Fact]
    public void Controllers_DoNotTakeTheInstanceQueryRepository()
    {
        Assert.Empty(
            ControllersTaking(typeof(IInstanceQueryRepository))
                .Except(_instanceQueryRepositoryUsers)
                .Select(type => type.Name)
        );
    }

    [Fact]
    public void InstanceQueryRepositoryUsers_StillTakeTheInstanceQueryRepository()
    {
        Assert.Empty(
            _instanceQueryRepositoryUsers
                .Except(ControllersTaking(typeof(IInstanceQueryRepository)))
                .Select(type => type.Name)
        );
    }

    private static IEnumerable<Type> ControllersTaking(params Type[] dependencies) =>
        typeof(InstancesController)
            .Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .Where(type =>
                type.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Any(parameter => dependencies.Contains(parameter.ParameterType))
            );

    private static IEnumerable<MethodInfo> GetInstanceActions() =>
        _instanceControllers.SelectMany(controller =>
            controller
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any())
        );

    private static string Describe(MethodInfo action)
    {
        if (action.GetCustomAttribute<InstanceScopeAccessAttribute>() is { } instanceScope)
        {
            return instanceScope.Access.ToString();
        }

        if (action.GetCustomAttribute<RequireScopeAttribute>() is { } fixedScope)
        {
            return $"{fixedScope.Scope} ({fixedScope.Access})";
        }

        return action.GetCustomAttribute<ExcludeFromScopeCheckAttribute>() is { } excluded
            ? $"excluded: {excluded.Reason}"
            : "missing";
    }
}
