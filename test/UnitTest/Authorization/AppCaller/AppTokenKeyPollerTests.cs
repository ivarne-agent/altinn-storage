using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization.AppCaller;
using Altinn.Platform.Storage.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Authorization.AppCaller;

public class AppTokenKeyPollerTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-30T12:00:00Z"));
    private readonly FakeKeySource _source = new();
    private readonly AppTokenKeySet _keySet;
    private readonly AppTokenKeyPoller _poller;

    public AppTokenKeyPollerTests()
    {
        _keySet = new AppTokenKeySet(_time);
        _poller = new AppTokenKeyPoller(
            _source,
            _keySet,
            Options.Create(new AppCallerSettings { PollIntervalSeconds = 300 }),
            _time,
            NullLogger<AppTokenKeyPoller>.Instance
        );
    }

    [Fact]
    public async Task PollOnceAsync_NewVersionAppears_IsAvailableAfterPoll()
    {
        _source.Next = () => [Key("ttd", "v1")];
        Assert.True(await _poller.PollOnceAsync(CancellationToken.None));
        Assert.False(_keySet.TryGetKey("ttd", "v2", out _));

        _source.Next = () => [Key("ttd", "v1"), Key("ttd", "v2")];
        Assert.True(await _poller.PollOnceAsync(CancellationToken.None));

        Assert.True(_keySet.TryGetKey("ttd", "v1", out _));
        Assert.True(_keySet.TryGetKey("ttd", "v2", out _));
    }

    [Fact]
    public async Task PollOnceAsync_SourceFails_KeepsPreviousSet()
    {
        _source.Next = () => [Key("ttd", "v1")];
        await _poller.PollOnceAsync(CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));

        _source.Next = () => throw new InvalidOperationException("Key Vault unavailable");
        bool replaced = await _poller.PollOnceAsync(CancellationToken.None);

        Assert.False(replaced);
        Assert.True(_keySet.TryGetKey("ttd", "v1", out _));
        Assert.Equal(TimeSpan.FromMinutes(5), _keySet.KeySetAge);
    }

    [Fact]
    public async Task ExecuteAsync_PollsOnStartAndEveryInterval_AndRetriesFailuresSooner()
    {
        int calls = 0;
        _source.Next = () =>
            Interlocked.Increment(ref calls) switch
            {
                1 => throw new InvalidOperationException("Key Vault unavailable"),
                2 => [Key("ttd", "v1")],
                _ => [Key("ttd", "v1"), Key("ttd", "v2")],
            };

        await _poller.StartAsync(CancellationToken.None);
        try
        {
            await WaitFor(() => _source.Calls >= 1, TimeSpan.Zero);
            Assert.Null(_keySet.KeySetAge);

            await WaitFor(() => _source.Calls >= 2, TimeSpan.FromSeconds(30));
            Assert.True(_keySet.TryGetKey("ttd", "v1", out _));
            Assert.False(_keySet.TryGetKey("ttd", "v2", out _));

            await WaitFor(() => _source.Calls >= 3, TimeSpan.FromSeconds(300));
            Assert.True(_keySet.TryGetKey("ttd", "v2", out _));
        }
        finally
        {
            await _poller.StopAsync(CancellationToken.None);
        }
    }

    private static AppTokenSigningKey Key(string org, string kid) =>
        new(org, kid, AppTokenTestHelper.CreateEcKey(kid));

    /// <summary>
    /// Advances the fake clock by <paramref name="step"/> until <paramref name="condition"/> holds.
    /// The step is repeated because the poller may register its delay after an advance.
    /// </summary>
    private async Task WaitFor(Func<bool> condition, TimeSpan step)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the poller");
            await Task.Delay(10);
            if (!condition() && step > TimeSpan.Zero && _source.Waiting)
            {
                _time.Advance(step);
            }
        }

        // Let the poller finish swapping and start its next delay.
        await Task.Delay(20);
    }

    private sealed class FakeKeySource : IAppTokenKeySource
    {
        private int _calls;

        public Func<IReadOnlyList<AppTokenSigningKey>> Next { get; set; } = () => [];

        public int Calls => Volatile.Read(ref _calls);

        public bool Waiting { get; private set; }

        public Task<IReadOnlyList<AppTokenSigningKey>> GetKeysAsync(
            CancellationToken cancellationToken
        )
        {
            Waiting = false;
            try
            {
                return Task.FromResult(Next());
            }
            finally
            {
                Interlocked.Increment(ref _calls);
                Waiting = true;
            }
        }
    }
}
