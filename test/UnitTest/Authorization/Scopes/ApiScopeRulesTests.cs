using System.Collections.Generic;
using System.Security.Claims;
using Altinn.Platform.Storage.Authorization.Scopes;
using Altinn.Platform.Storage.Interface.Models;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.Scopes;

public class ApiScopeRulesTests
{
    private const string AppUserRead = "test:[app]/instances.read";
    private const string AppUserWrite = "test:[app]/instances.write";
    private const string AppServiceOwnerRead = "test:[org]/[app]/serviceowner.read";
    private const string AppServiceOwnerWrite = "test:[org]/[app]/serviceowner.write";

    public enum Caller
    {
        User,
        SystemUser,
        ServiceOwner,
    }

    /// <summary>
    /// The scope the token carries, relative to the endpoint's level and the caller.
    /// </summary>
    public enum TokenScope
    {
        /// <summary>The scope the app defines for the caller type and level (resolved).</summary>
        App,

        /// <summary>The built-in scope for the caller type and level.</summary>
        BuiltIn,

        /// <summary>The built-in scope for the caller type at the other level.</summary>
        BuiltInOtherLevel,

        /// <summary>altinn:portal/enduser.</summary>
        Portal,

        /// <summary>An unrelated scope.</summary>
        Wrong,

        /// <summary>The sync adapter scope.</summary>
        SyncAdapter,
    }

    /// <summary>
    /// Caller type × app with/without apiScopes × token scope, with the expected outcome. Each row runs
    /// for Read and Write, and in Observe and Enforce.
    /// </summary>
    public static TheoryData<Caller, bool, TokenScope, ScopeCheckOutcome> Table()
    {
        TheoryData<Caller, bool, TokenScope, ScopeCheckOutcome> data = new();
        foreach (Caller user in new[] { Caller.User, Caller.SystemUser })
        {
            data.Add(user, false, TokenScope.App, ScopeCheckOutcome.MissingBuiltIn);
            data.Add(user, false, TokenScope.BuiltIn, ScopeCheckOutcome.Ok);
            data.Add(user, false, TokenScope.BuiltInOtherLevel, ScopeCheckOutcome.MissingBuiltIn);
            data.Add(user, false, TokenScope.Portal, ScopeCheckOutcome.Ok);
            data.Add(user, false, TokenScope.Wrong, ScopeCheckOutcome.MissingBuiltIn);
            data.Add(user, false, TokenScope.SyncAdapter, ScopeCheckOutcome.SyncAdapter);
            data.Add(user, true, TokenScope.App, ScopeCheckOutcome.Ok);
            data.Add(user, true, TokenScope.BuiltIn, ScopeCheckOutcome.MissingApp);
            data.Add(user, true, TokenScope.BuiltInOtherLevel, ScopeCheckOutcome.MissingApp);
            data.Add(user, true, TokenScope.Portal, ScopeCheckOutcome.Ok);
            data.Add(user, true, TokenScope.Wrong, ScopeCheckOutcome.MissingApp);
            data.Add(user, true, TokenScope.SyncAdapter, ScopeCheckOutcome.SyncAdapter);
        }

        Caller so = Caller.ServiceOwner;
        data.Add(so, false, TokenScope.App, ScopeCheckOutcome.MissingBuiltIn);
        data.Add(so, false, TokenScope.BuiltIn, ScopeCheckOutcome.OkServiceOwnerBuiltIn);
        data.Add(so, false, TokenScope.BuiltInOtherLevel, ScopeCheckOutcome.MissingBuiltIn);
        data.Add(so, false, TokenScope.Portal, ScopeCheckOutcome.MissingBuiltIn);
        data.Add(so, false, TokenScope.Wrong, ScopeCheckOutcome.MissingBuiltIn);
        data.Add(so, false, TokenScope.SyncAdapter, ScopeCheckOutcome.SyncAdapter);
        data.Add(so, true, TokenScope.App, ScopeCheckOutcome.OkServiceOwnerApp);
        data.Add(so, true, TokenScope.BuiltIn, ScopeCheckOutcome.OkServiceOwnerBuiltIn);
        data.Add(so, true, TokenScope.BuiltInOtherLevel, ScopeCheckOutcome.MissingApp);
        data.Add(so, true, TokenScope.Portal, ScopeCheckOutcome.MissingApp);
        data.Add(so, true, TokenScope.Wrong, ScopeCheckOutcome.MissingApp);
        data.Add(so, true, TokenScope.SyncAdapter, ScopeCheckOutcome.SyncAdapter);
        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Decide_FollowsTheScopeTable(
        Caller caller,
        bool appHasApiScopes,
        TokenScope tokenScope,
        ScopeCheckOutcome expected
    )
    {
        foreach (ScopeAccess access in new[] { ScopeAccess.Read, ScopeAccess.Write })
        {
            foreach (
                ScopeCheckMode mode in new[] { ScopeCheckMode.Observe, ScopeCheckMode.Enforce }
            )
            {
                ClaimsPrincipal user = CreateUser(caller, ScopeFor(caller, tokenScope, access));
                ScopeDecision decision = ApiScopeRules.Decide(
                    ApiScopeRules.GetScopes(user),
                    caller == Caller.ServiceOwner,
                    access,
                    CreateApp(appHasApiScopes),
                    mode,
                    mode,
                    ApiScopeRules.SyncAdapter
                );

                Assert.Equal(expected, decision.Outcome);
                Assert.Equal(access, decision.Access);
                Assert.Equal(mode, decision.Mode);
                Assert.Equal(
                    mode == ScopeCheckMode.Enforce && decision.IsMissing,
                    decision.Refuses
                );
                Assert.True(decision.IsObserved);
            }
        }
    }

    /// <summary>
    /// Mixed modes on an app with its own scopes: built-in mode, app mode, caller, token scope, the expected
    /// outcome and mode, and whether the request is refused. Each row runs for Read and Write.
    /// </summary>
    public static TheoryData<
        ScopeCheckMode,
        ScopeCheckMode,
        Caller,
        TokenScope,
        ScopeCheckOutcome,
        ScopeCheckMode,
        bool
    > MixedModes()
    {
        const ScopeCheckMode Off = ScopeCheckMode.Off;
        const ScopeCheckMode Observe = ScopeCheckMode.Observe;
        const ScopeCheckMode Enforce = ScopeCheckMode.Enforce;
        const ScopeCheckOutcome Missing = ScopeCheckOutcome.MissingApp;

        TheoryData<
            ScopeCheckMode,
            ScopeCheckMode,
            Caller,
            TokenScope,
            ScopeCheckOutcome,
            ScopeCheckMode,
            bool
        > data = new();
        foreach (Caller user in new[] { Caller.User, Caller.SystemUser })
        {
            // Built-in Enforce, app Observe or Off.
            data.Add(Enforce, Observe, user, TokenScope.Wrong, Missing, Enforce, true);
            data.Add(Enforce, Observe, user, TokenScope.BuiltIn, Missing, Observe, false);
            data.Add(Enforce, Observe, user, TokenScope.BuiltInOtherLevel, Missing, Enforce, true);
            data.Add(
                Enforce,
                Observe,
                user,
                TokenScope.Portal,
                ScopeCheckOutcome.Ok,
                Observe,
                false
            );
            data.Add(Enforce, Observe, user, TokenScope.App, ScopeCheckOutcome.Ok, Observe, false);
            data.Add(
                Enforce,
                Off,
                user,
                TokenScope.Wrong,
                ScopeCheckOutcome.MissingBuiltIn,
                Enforce,
                true
            );
            data.Add(Enforce, Off, user, TokenScope.BuiltIn, ScopeCheckOutcome.Ok, Enforce, false);
            data.Add(
                Enforce,
                Off,
                user,
                TokenScope.App,
                ScopeCheckOutcome.MissingBuiltIn,
                Enforce,
                true
            );

            // App Enforce, built-in Observe or Off.
            data.Add(Observe, Enforce, user, TokenScope.Wrong, Missing, Enforce, true);
            data.Add(Observe, Enforce, user, TokenScope.BuiltIn, Missing, Enforce, true);
            data.Add(Observe, Enforce, user, TokenScope.App, ScopeCheckOutcome.Ok, Enforce, false);
            data.Add(Off, Enforce, user, TokenScope.Wrong, Missing, Enforce, true);
            data.Add(Off, Enforce, user, TokenScope.BuiltIn, Missing, Enforce, true);

            // Built-in Observe, app Off or Observe never refuse.
            data.Add(
                Observe,
                Off,
                user,
                TokenScope.Wrong,
                ScopeCheckOutcome.MissingBuiltIn,
                Observe,
                false
            );
            data.Add(Off, Observe, user, TokenScope.Wrong, Missing, Observe, false);
        }

        Caller so = Caller.ServiceOwner;
        data.Add(Enforce, Observe, so, TokenScope.Wrong, Missing, Enforce, true);
        data.Add(Enforce, Observe, so, TokenScope.BuiltInOtherLevel, Missing, Enforce, true);
        data.Add(
            Enforce,
            Observe,
            so,
            TokenScope.BuiltIn,
            ScopeCheckOutcome.OkServiceOwnerBuiltIn,
            Enforce,
            false
        );
        data.Add(
            Enforce,
            Observe,
            so,
            TokenScope.App,
            ScopeCheckOutcome.OkServiceOwnerApp,
            Observe,
            false
        );
        data.Add(
            Enforce,
            Off,
            so,
            TokenScope.Wrong,
            ScopeCheckOutcome.MissingBuiltIn,
            Enforce,
            true
        );
        data.Add(Enforce, Off, so, TokenScope.App, ScopeCheckOutcome.MissingBuiltIn, Enforce, true);
        data.Add(Observe, Enforce, so, TokenScope.Wrong, Missing, Enforce, true);
        data.Add(
            Off,
            Enforce,
            so,
            TokenScope.BuiltIn,
            ScopeCheckOutcome.OkServiceOwnerBuiltIn,
            Off,
            false
        );
        data.Add(
            Off,
            Enforce,
            so,
            TokenScope.App,
            ScopeCheckOutcome.OkServiceOwnerApp,
            Enforce,
            false
        );
        data.Add(Off, Observe, so, TokenScope.Wrong, Missing, Observe, false);
        return data;
    }

    [Theory]
    [MemberData(nameof(MixedModes))]
    public void Decide_MixedModes_NeverProtectsAppScopesLessThanBuiltIn(
        ScopeCheckMode builtInMode,
        ScopeCheckMode appMode,
        Caller caller,
        TokenScope tokenScope,
        ScopeCheckOutcome expectedOutcome,
        ScopeCheckMode expectedMode,
        bool expectedRefuses
    )
    {
        foreach (ScopeAccess access in new[] { ScopeAccess.Read, ScopeAccess.Write })
        {
            ClaimsPrincipal user = CreateUser(caller, ScopeFor(caller, tokenScope, access));
            ScopeDecision decision = ApiScopeRules.Decide(
                ApiScopeRules.GetScopes(user),
                caller == Caller.ServiceOwner,
                access,
                CreateApp(true),
                builtInMode,
                appMode,
                ApiScopeRules.SyncAdapter
            );

            Assert.Equal(expectedOutcome, decision.Outcome);
            Assert.Equal(expectedMode, decision.Mode);
            Assert.Equal(expectedRefuses, decision.Refuses);
        }
    }

    [Fact]
    public void Decide_AppScopeOfOneApp_DoesNotGiveAccessToAnother()
    {
        ScopeDecision decision = ApiScopeRules.Decide(
            new HashSet<string> { "test:app-a/instances.read" },
            false,
            ScopeAccess.Read,
            CreateApp(true, "app-b"),
            ScopeCheckMode.Enforce,
            ScopeCheckMode.Enforce,
            ApiScopeRules.SyncAdapter
        );

        Assert.Equal(ScopeCheckOutcome.MissingApp, decision.Outcome);
        Assert.True(decision.Refuses);
    }

    [Fact]
    public void Decide_UnknownApp_RequiresBuiltInScope()
    {
        ScopeDecision decision = ApiScopeRules.Decide(
            new HashSet<string> { "test:app-a/instances.read" },
            false,
            ScopeAccess.Read,
            null,
            ScopeCheckMode.Enforce,
            ScopeCheckMode.Enforce,
            ApiScopeRules.SyncAdapter
        );

        Assert.Equal(ScopeCheckOutcome.MissingBuiltIn, decision.Outcome);
    }

    [Fact]
    public void Decide_AppModeOff_IgnoresTheAppsScopes()
    {
        ScopeDecision decision = ApiScopeRules.Decide(
            new HashSet<string> { ApiScopeRules.InstancesRead },
            false,
            ScopeAccess.Read,
            CreateApp(true),
            ScopeCheckMode.Enforce,
            ScopeCheckMode.Off,
            ApiScopeRules.SyncAdapter
        );

        Assert.Equal(ScopeCheckOutcome.Ok, decision.Outcome);
        Assert.Equal(ScopeCheckMode.Enforce, decision.Mode);
    }

    [Fact]
    public void Decide_MissingAppScope_IsGovernedByTheAppMode()
    {
        ScopeDecision decision = ApiScopeRules.Decide(
            new HashSet<string> { ApiScopeRules.InstancesRead },
            false,
            ScopeAccess.Read,
            CreateApp(true),
            ScopeCheckMode.Enforce,
            ScopeCheckMode.Observe,
            ApiScopeRules.SyncAdapter
        );

        Assert.Equal(ScopeCheckOutcome.MissingApp, decision.Outcome);
        Assert.False(decision.Refuses);
    }

    [Fact]
    public void Decide_BuiltInModeOff_NeitherRecordsNorRefuses()
    {
        ScopeDecision decision = ApiScopeRules.Decide(
            new HashSet<string>(),
            false,
            ScopeAccess.Write,
            null,
            ScopeCheckMode.Off,
            ScopeCheckMode.Enforce,
            ApiScopeRules.SyncAdapter
        );

        Assert.Equal(ScopeCheckOutcome.MissingBuiltIn, decision.Outcome);
        Assert.False(decision.IsObserved);
        Assert.False(decision.Refuses);
    }

    [Fact]
    public void Decide_AppDefinesOnlyWriteScope_ReadUsesBuiltInScope()
    {
        Application app = CreateApp(false);
        app.ApiScopes = new ApiScopesConfiguration
        {
            Users = new ApiScopes { Write = AppUserWrite },
        };

        ScopeDecision read = ApiScopeRules.Decide(
            new HashSet<string> { ApiScopeRules.InstancesRead },
            false,
            ScopeAccess.Read,
            app,
            ScopeCheckMode.Enforce,
            ScopeCheckMode.Enforce,
            ApiScopeRules.SyncAdapter
        );

        Assert.Equal(ScopeCheckOutcome.Ok, read.Outcome);
    }

    [Theory]
    [InlineData("altinn:instances.read", true)]
    [InlineData("other altinn:instances.read\tmore", true)]
    [InlineData("  altinn:instances.read  ", true)]
    [InlineData("ALTINN:INSTANCES.READ", false)]
    [InlineData("altinn:instances.readonly", false)]
    [InlineData("altinn:instances", false)]
    [InlineData("", false)]
    public void GetScopes_SplitsOnWhitespaceAndComparesOrdinally(string claim, bool expected)
    {
        ClaimsPrincipal user = new(new ClaimsIdentity([new Claim("scope", claim)], "test"));

        Assert.Equal(expected, ApiScopeRules.GetScopes(user).Contains(ApiScopeRules.InstancesRead));
    }

    [Fact]
    public void GetScopes_PrefersAltinnScopeClaim()
    {
        ClaimsPrincipal user = new(
            new ClaimsIdentity(
                [
                    new Claim("urn:altinn:scope", ApiScopeRules.InstancesWrite),
                    new Claim("scope", ApiScopeRules.InstancesRead),
                ],
                "test"
            )
        );

        IReadOnlySet<string> scopes = ApiScopeRules.GetScopes(user);

        Assert.Contains(ApiScopeRules.InstancesWrite, scopes);
        Assert.DoesNotContain(ApiScopeRules.InstancesRead, scopes);
    }

    [Theory]
    [InlineData("test:[app]/instances.read", "test:my-app/instances.read")]
    [InlineData("[org]:[app]/[app]", "ttd:my-app/my-app")]
    [InlineData(" test:static ", "test:static")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ResolvePlaceholders_ReplacesAppAndOrg(string? scope, string? expected)
    {
        Assert.Equal(expected, ApiScopeRules.ResolvePlaceholders(scope, "ttd", "my-app"));
    }

    private static string ScopeFor(Caller caller, TokenScope tokenScope, ScopeAccess access)
    {
        bool read = access == ScopeAccess.Read;
        bool serviceOwner = caller == Caller.ServiceOwner;
        return tokenScope switch
        {
            TokenScope.App => serviceOwner
                ? (
                    read
                        ? "test:ttd/my-app/serviceowner.read"
                        : "test:ttd/my-app/serviceowner.write"
                )
                : (read ? "test:my-app/instances.read" : "test:my-app/instances.write"),
            TokenScope.BuiltIn => BuiltIn(serviceOwner, read),
            TokenScope.BuiltInOtherLevel => BuiltIn(serviceOwner, !read),
            TokenScope.Portal => ApiScopeRules.PortalEndUser,
            TokenScope.Wrong => "altinn:something.else",
            TokenScope.SyncAdapter => ApiScopeRules.SyncAdapter,
            _ => string.Empty,
        };
    }

    private static string BuiltIn(bool serviceOwner, bool read)
    {
        if (serviceOwner)
        {
            return read
                ? ApiScopeRules.ServiceOwnerInstancesRead
                : ApiScopeRules.ServiceOwnerInstancesWrite;
        }

        return read ? ApiScopeRules.InstancesRead : ApiScopeRules.InstancesWrite;
    }

    private static ClaimsPrincipal CreateUser(Caller caller, string scope)
    {
        List<Claim> claims = caller switch
        {
            Caller.ServiceOwner =>
            [
                new Claim("urn:altinn:org", "ttd"),
                new Claim("urn:altinn:scope", scope),
            ],
            Caller.SystemUser =>
            [
                new Claim(
                    "authorization_details",
                    "{\"type\":\"urn:altinn:systemuser\",\"systemuser_id\":[\"f58fe166-bc22-4899-beb7-c3e8e3332f43\"],\"systemuser_org\":{\"authority\":\"iso6523-actorid-upis\",\"ID\":\"0192:991825827\"},\"system_id\":\"system\"}"
                ),
                new Claim("scope", scope),
            ],
            _ => [new Claim("urn:altinn:userid", "1337"), new Claim("scope", scope)],
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static Application CreateApp(bool withApiScopes, string app = "my-app")
    {
        Application application = new() { Id = $"ttd/{app}", Org = "ttd" };
        if (withApiScopes)
        {
            application.ApiScopes = new ApiScopesConfiguration
            {
                Users = new ApiScopes { Read = AppUserRead, Write = AppUserWrite },
                ServiceOwners = new ApiScopes
                {
                    Read = AppServiceOwnerRead,
                    Write = AppServiceOwnerWrite,
                },
            };
        }

        return application;
    }
}
