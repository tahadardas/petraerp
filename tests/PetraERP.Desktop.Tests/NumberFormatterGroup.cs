namespace PetraERP.Desktop.Tests;

/// <summary>
/// Serializes the tests that read or set the process-wide number formatter. PetraNumberFormat keeps
/// its display mode and culture in static state, so a class that configures Arabic-Indic separators must never overlap a class
/// that asserts English ones.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NumberFormatterGroup
{
    public const string Name = "Number formatter";
}
