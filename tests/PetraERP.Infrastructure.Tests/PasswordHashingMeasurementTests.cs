using System.Diagnostics;
using System.Globalization;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

/// <summary>Explicit measurement of the production password hasher (PBKDF2-SHA512) on this machine. Not a gate.</summary>
public sealed class PasswordHashingMeasurementTests
{
    [Fact(Explicit = true)]
    public void MeasureHashAndVerifyLatency()
    {
        var hasher = new Pbkdf2PasswordHasher(AuthenticationSettings.Default);
        var stored = hasher.Hash("Measure-Secure-2026!");
        _ = hasher.Verify("Measure-Secure-2026!", stored);
        var samples = new List<double>();
        for (var index = 0; index < 20; index++)
        {
            var timer = Stopwatch.StartNew();
            Assert.True(hasher.Verify("Measure-Secure-2026!", stored));
            samples.Add(timer.Elapsed.TotalMilliseconds);
        }

        var sorted = samples.Order().ToArray();
        var line = FormattableString.Invariant($"verify_ms;p50;{sorted[9]:F1};p95;{sorted[18]:F1};max;{sorted[^1]:F1};iterations;{stored.Iterations}");
        File.WriteAllText(Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_HASH") ?? Path.Combine(Path.GetTempPath(), "petra_hash_bench.txt"),
            line, System.Text.Encoding.UTF8);
        Assert.Equal(AuthenticationSettings.MinimumPasswordIterations, stored.Iterations);
        _ = CultureInfo.InvariantCulture;
    }
}
