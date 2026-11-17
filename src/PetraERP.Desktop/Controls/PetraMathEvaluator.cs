using System.Globalization;
using System.Text;

namespace PetraERP.Desktop.Controls;

/// <summary>
/// Safe, deterministic arithmetic expression evaluator for inline financial math inside numeric boxes and DataGrid cells.
/// Supports basic operations (+, -, *, /, ×, ÷), grouping parentheses, decimals, Arabic-Indic digits, and exact decimal arithmetic.
/// Prevents zero-division, overflow, and script execution vulnerabilities.
/// </summary>
public static class PetraMathEvaluator
{
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
                if (prev >= 0 && (char.IsAsciiDigit(trimmed[prev]) || trimmed[prev] is ')' or '٠' or '١' or '٢' or '٣' or '٤' or '٥' or '٦' or '٧' or '٨' or '٩'))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates an arithmetic expression into a decimal result.
    /// Returns false if the expression is syntactically invalid, divides by zero, or overflows.
    /// </summary>
    public static bool TryEvaluate(string? expression, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(expression)) return false;

        var normalized = NormalizeExpression(expression);
        if (normalized.Length == 0) return false;

        try
        {
            var parser = new MathParser(normalized);
            result = parser.Parse();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeExpression(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch is '\u061c' or '\u200e' or '\u200f') continue;
            if (ch is >= '٠' and <= '٩') sb.Append((char)('0' + ch - '٠'));
            else if (ch is >= '۰' and <= '۹') sb.Append((char)('0' + ch - '۰'));
            else if (ch is '٫' or ',') sb.Append('.');
            else if (ch is '×') sb.Append('*');
            else if (ch is '÷') sb.Append('/');
            else if (ch is '−') sb.Append('-');
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private sealed class MathParser
    {
        private readonly string text;
        private int pos;

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
                if (pos >= text.Length || text[pos] != ')')
                {
                    throw new FormatException("Missing closing parenthesis.");
                }
                pos++;
                return value;
            }

            // Number
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

            var span = text.AsSpan(start, pos - start);
            return decimal.Parse(span, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
