using System.Buffers;
using System.Text;

namespace PetraERP.Domain.Primitives;

internal static class DomainText
{
    internal static string Required(string value, int maximumLength, string parameterName, bool multiline = false)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maximumLength)
        {
            throw new ArgumentException("Text is empty or exceeds its supported length.", parameterName);
        }

        var remaining = trimmed.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done
                || (Rune.IsControl(rune) && !(multiline && rune.Value is '\r' or '\n' or '\t')))
            {
                throw new ArgumentException("Text contains malformed Unicode or unsupported control characters.", parameterName);
            }

            remaining = remaining[consumed..];
        }

        return trimmed;
    }
}
