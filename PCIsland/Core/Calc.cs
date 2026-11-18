using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PCIsland.Core;

public static class Calc
{
    public static bool LooksLikeMath(string s)
    {
        s = s.Trim();
        if (s.Length < 2) return false;
        if (Regex.IsMatch(Text.Fold(s), @"^%?\d+([.,]\d+)?%?\s*(of|nin|nun|in|un|'nin|'in|'un|'nun)?\s*%?\d")) { if (PercentOf(s, out _)) return true; }
        if (!Regex.IsMatch(s, @"^[\d\s.,+\-*/x×÷^%()=]+$")) return false;
        if (!s.Any(char.IsDigit)) return false;
        return s.Any(c => "+-*/x×÷^%".Contains(c)) && !Regex.IsMatch(s, @"^\d+$");
    }

    static bool PercentOf(string s, out double result)
    {
        result = 0;
        var f = Text.Fold(s).Replace("'", " ");
        var m = Regex.Match(f, @"^%?\s*(\d+(?:[.,]\d+)?)\s*%?\s*(?:of)\s*(\d+(?:[.,]\d+)?)$");
        if (m.Success) { result = Num(m.Groups[1].Value) / 100 * Num(m.Groups[2].Value); return true; }
        m = Regex.Match(f, @"^(\d+(?:[.,]\d+)?)\s*(?:nin|nun|in|un|nin|n[iu]n)?\s*%\s*(\d+(?:[.,]\d+)?)\s*(?:i|u|si|su|'i)?$");
        if (m.Success) { result = Num(m.Groups[2].Value) / 100 * Num(m.Groups[1].Value); return true; }
        return false;
    }

    static double Num(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    public static bool TryEval(string input, out double value)
    {
        value = 0;
        try
        {
            var s = input.Trim().TrimEnd('=').Trim();
            if (PercentOf(s, out value)) return true;
            s = s.Replace('x', '*').Replace('×', '*').Replace('÷', '/').Replace(" ", "");
            s = Regex.Replace(s, @"(\d),(\d)", "$1.$2");
            int pos = 0;
            value = Expr(s, ref pos);
            if (pos != s.Length || double.IsNaN(value) || double.IsInfinity(value)) return false;
            return true;
        }
        catch { return false; }
    }

    public static string Format(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-9 && Math.Abs(v) < 1e15) return Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");
        return Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
    }

    static double Expr(string s, ref int i)
    {
        double v = Term(s, ref i);
        while (i < s.Length && (s[i] == '+' || s[i] == '-'))
        {
            char op = s[i++];
            double r = Term(s, ref i);
            v = op == '+' ? v + r : v - r;
        }
        return v;
    }

    static double Term(string s, ref int i)
    {
        double v = Power(s, ref i);
        while (i < s.Length && (s[i] == '*' || s[i] == '/' || (s[i] == '%' && i + 1 < s.Length && (char.IsDigit(s[i + 1]) || s[i + 1] == '('))))
        {
            char op = s[i++];
            double r = Power(s, ref i);
            v = op == '*' ? v * r : op == '/' ? v / r : v % r;
        }
        return v;
    }

    static double Power(string s, ref int i)
    {
        double b = Unary(s, ref i);
        if (i < s.Length && s[i] == '^') { i++; return Math.Pow(b, Power(s, ref i)); }
        return b;
    }

    static double Unary(string s, ref int i)
    {
        if (i < s.Length && s[i] == '-') { i++; return -Unary(s, ref i); }
        if (i < s.Length && s[i] == '+') { i++; return Unary(s, ref i); }
        return Atom(s, ref i);
    }

    static double Atom(string s, ref int i)
    {
        double v;
        if (i < s.Length && s[i] == '(')
        {
            i++;
            v = Expr(s, ref i);
            if (i >= s.Length || s[i] != ')') throw new FormatException();
            i++;
        }
        else
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            if (start == i) throw new FormatException();
            v = double.Parse(s[start..i], CultureInfo.InvariantCulture);
        }
        if (i < s.Length && s[i] == '%' && (i + 1 == s.Length || !char.IsDigit(s[i + 1]) && s[i + 1] != '(')) { i++; v /= 100; }
        return v;
    }
}
