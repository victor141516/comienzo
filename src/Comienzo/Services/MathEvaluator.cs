using System.Globalization;

namespace Comienzo.Services;

internal static class MathEvaluator
{
    public static bool TryEvaluate(string input, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input) || !input.Any(char.IsDigit) ||
            !input.Any(c => "+-*/^()".Contains(c)) ||
            input.Any(c => !char.IsDigit(c) && !char.IsWhiteSpace(c) && !"+-*/^().,".Contains(c)))
            return false;

        try
        {
            var parser = new Parser(input.Replace(',', '.'));
            value = parser.ParseExpression();
            parser.SkipWhiteSpace();
            return parser.AtEnd && double.IsFinite(value);
        }
        catch
        {
            return false;
        }
    }

    public static string Format(double value) => value.ToString("G15", CultureInfo.CurrentCulture);

    private sealed class Parser(string text)
    {
        private int _position;
        public bool AtEnd => _position == text.Length;

        public double ParseExpression()
        {
            double value = ParseTerm();
            while (true)
            {
                SkipWhiteSpace();
                if (Take('+')) value += ParseTerm();
                else if (Take('-')) value -= ParseTerm();
                else return value;
            }
        }

        private double ParseTerm()
        {
            double value = ParseUnary();
            while (true)
            {
                SkipWhiteSpace();
                if (Take('*')) value *= ParseUnary();
                else if (Take('/'))
                {
                    double divisor = ParseUnary();
                    if (divisor == 0) throw new DivideByZeroException();
                    value /= divisor;
                }
                else return value;
            }
        }

        private double ParseUnary()
        {
            SkipWhiteSpace();
            if (Take('+')) return ParseUnary();
            if (Take('-')) return -ParseUnary();
            return ParsePower();
        }

        private double ParsePower()
        {
            double left = ParsePrimary();
            SkipWhiteSpace();
            return Take('^') ? Math.Pow(left, ParseUnary()) : left;
        }

        private double ParsePrimary()
        {
            SkipWhiteSpace();
            if (Take('('))
            {
                double value = ParseExpression();
                SkipWhiteSpace();
                if (!Take(')')) throw new FormatException();
                return value;
            }

            int start = _position;
            while (_position < text.Length && (char.IsDigit(text[_position]) || text[_position] == '.'))
                _position++;
            if (start == _position || !double.TryParse(text[start.._position], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double number))
                throw new FormatException();
            return number;
        }

        private bool Take(char value)
        {
            if (_position >= text.Length || text[_position] != value) return false;
            _position++;
            return true;
        }

        public void SkipWhiteSpace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position])) _position++;
        }
    }
}
