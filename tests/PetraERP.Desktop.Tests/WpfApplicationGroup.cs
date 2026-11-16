namespace PetraERP.Desktop.Tests;

/// <summary>Serializes tests that own or depend on the single process-wide WPF Application.</summary>
[CollectionDefinition(Name)]
public sealed class WpfApplicationGroup
{
    public const string Name = "WPF application";
}
