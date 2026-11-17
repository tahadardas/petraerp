using System.Globalization;
using System.Text;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Safe, deterministic arithmetic expression evaluator for inline financial math inside numeric boxes and DataGrid cells.
/// Supports +, -, *, /, ×, ÷, grouping parentheses, decimals, Arabic-Indic digits and exact decimal arithmetic.
/// <para>
/// Separators follow the Petra number settings, never a guess: every operand is normalized by the same rules as a plain number
/// box (<see cref="PetraNumberFormat.NormalizeInputWith"/>), so "," is a thousands separator or a decimal comma exactly as the user
/// configured, and an operand whose separators are ambiguous or malformed under those settings (for example "1,5" while "," groups
/// thousands) makes the whole expression invalid instead of silently computing a different amount.
/// </para>
/// Prevents zero-division, overflow, runaway nesting and script execution.
/// </summary>
public static class PetraMathEvaluator
{
    private const int MaximumLength = 256;
    private const int MaximumDepth = 32;
    private static readonly char[] MathOperators = ['+', '*', '/', '×', '÷'];

    /// <summary>
    /// Checks if the text contains explicit arithmetic operators requiring evaluation.
    /// Leading negative signs or simple accounting parentheses (e.g. "(100)") are excluded to preserve standard negative number parsing.
    /// </summary>
    public static bool ContainsOperator(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.IndexOfAny(MathOperators) >= 0) return true;

        // Check for binary minus (minus preceded by a digit or closing parenthesis, not just a leading sign)
        for (var i = 1; i < trimmed.Length; i++)
        {
            if (trimmed[i] is '-' or '−')
            {
                // Look backward for non-whitespace
                var prev = i - 1;
                while (prev >= 0 && char.IsWhiteSpace(trimmed[prev])) prev--;
                if (prev >= 0 && (IsDigit(trimmed[prev]) || trimmed[prev] == ')')) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates an arithmetic expression into a decimal result using the current Petra number settings.
    /// Returns false if the expression is syntactically invalid, has ambiguous separators, divides by zero, or overflows.
    /// </summary>
    public static bool TryEvaluate(string? expression, out decimal result) =>
        TryEvaluateWith(expression, PetraNumberFormat.Mode, PetraNumberFormat.CurrentCulture, PetraNumberFormat.CurrentDecimalMode, out result);

    /// <summary>Same as <see cref="TryEvaluate"/> with explicit settings and no shared state (previews and tests).</summary>
    internal static bool TryEvaluateWith(string? expression, NumberDisplayMode displayMode, CultureInfo uiCulture, DecimalDisplayMode decimalMode, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > MaximumLength) return false;

        var normalized = NormalizeExpression(expression, displayMode, uiCulture, decimalMode);
        if (normalized.Length == 0) return false;

        try
        {
            result = new MathParser(normalized).Parse();
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or DivideByZeroException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsDigit(char ch) => char.IsAsciiDigit(ch) || ch is >= '٠' and <= '٩' or >= '۰' and <= '۹';

    /// <summary>Characters that can belong to ONE number: digits, both decimal separators, grouping marks and the invisible bidi controls.</summary>
    private static bool IsNumberCharacter(char ch) =>
        IsDigit(ch) || ch is '.' or ',' or '٫' or '٬' or ' ' or ' ' or ' ' or '؜' or '‎' or '‏';

    /// <summary>
    /// Rewrites the expression so every operand is an invariant number ("1234.5") and every operator is ASCII. Returns an empty string when
    /// any operand is refused by the number rules (ambiguous separator, wrong group size, stray characters).
    /// </summary>
    private static string NormalizeExpression(string text, NumberDisplayMode displayMode, CultureInfo uiCulture, DecimalDisplayMode decimalMode)
    {
        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch is '؜' or '‎' or '‏') { index++; continue; }
            if (IsNumberCharacter(ch) && !(ch is ' ' or ' ' or ' '))
            {
                // A number run: digits and separators, and spaces only when they sit between two number characters (grouping).
                var end = index;
                while (end < text.Length && IsNumberCharacter(text[end])) end++;
                var run = text[index..end].TrimEnd(' ', ' ', ' ');
                if (run.Length == 0) return string.Empty;
                var number = PetraNumberFormat.NormalizeInputWith(run, displayMode, uiCulture, decimalMode);
                if (number.Length == 0 || number.Contains('(') || number.Contains('-') || number.Contains('+')) return string.Empty;
                builder.Append(number);
                index += run.Length;
                continue;
            }

            if (char.IsWhiteSpace(ch)) { index++; continue; }
            switch (ch)
            {
                case '×': builder.Append('*'); break;
                case '÷': builder.Append('/'); break;
                case '−': builder.Append('-'); break;
                case '+' or '-' or '*' or '/' or '(' or ')': builder.Append(ch); break;
                default: return string.Empty;
            }

            index++;
        }

        return builder.ToString();
    }

    private sealed class MathParser
    {
        private readonly string text;
        private int pos;
        private int depth;

        public MathParser(string text)
        {
            this.text = text;
            pos = 0;
        }

        public decimal Parse()
        {
            var value = ParseExpression();
            if (pos < text.Length) throw new FormatException("Unexpected trailing characters.");
            return value;
        }

        private decimal ParseExpression()
        {
            var value = ParseTerm();
            while (pos < text.Length && text[pos] is '+' or '-')
            {
                var op = text[pos++];
                var next = ParseTerm();
                value = op == '+' ? checked(value + next) : checked(value - next);
            }
            return value;
        }

        private decimal ParseTerm()
        {
            var value = ParseFactor();
            while (pos < text.Length && text[pos] is '*' or '/')
            {
                var op = text[pos++];
                var next = ParseFactor();
                if (op == '/')
                {
                    if (next == 0m) throw new DivideByZeroException("Division by zero.");
                    value = checked(value / next);
                }
                else
                {
                    value = checked(value * next);
                }
            }
            return value;
        }

        private decimal ParseFactor()
        {
            if (pos >= text.Length) throw new FormatException("Unexpected end of expression.");
            if (++depth > MaximumDepth) throw new FormatException("Expression is nested too deeply.");
            try
            {
                // Unary + or -
                if (text[pos] == '+')
                {
                    pos++;
                    return ParseFactor();
                }

                if (text[pos] == '-')
                {
                    pos++;
                    return checked(-ParseFactor());
                }

                // Parentheses
                if (text[pos] == '(')
                {
                    pos++;
                    var value = ParseExpression();
                    if (pos >= text.Length || text[pos] != ')') throw new FormatException("Missing closing parenthesis.");
                    pos++;
                    return value;
                }

                // Number: already normalized to invariant digits with at most one decimal point.
                var start = pos;
                var hasDot = false;
                while (pos < text.Length && (char.IsAsciiDigit(text[pos]) || text[pos] == '.'))
                {
                    if (text[pos] == '.')
                    {
                        if (hasDot) throw new FormatException("Multiple decimal points in number.");
                        hasDot = true;
                    }
                    pos++;
                }

                if (start == pos) throw new FormatException("Expected number at position " + pos);
                return decimal.Parse(text.AsSpan(start, pos - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            }
            finally
            {
                depth--;
            }
        }
    }
}
