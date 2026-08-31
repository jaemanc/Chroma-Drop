// Json.cs — 최소 JSON 파서/빌더 (외부 라이브러리 의존 없음).
// Firebase RTDB 응답은 uid 를 키로 갖는 객체라 JsonUtility 로는 못 읽는다.
// 필요한 만큼만 구현: object / array / string / number / bool / null.

using System.Collections.Generic;
using System.Globalization;
using System.Text;

public static class Json
{
    /// <summary>파싱 실패 시 null 을 돌려준다 (예외를 던지지 않는다).</summary>
    public static object Parse(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        int i = 0;
        try
        {
            var v = ParseValue(s, ref i);
            return v;
        }
        catch (System.Exception) { return null; }
    }

    public static Dictionary<string, object> AsMap(object o) { return o as Dictionary<string, object>; }

    public static string Str(Dictionary<string, object> m, string key, string fallback)
    {
        object v;
        if (m != null && m.TryGetValue(key, out v) && v is string) return (string)v;
        return fallback;
    }

    public static long Num(Dictionary<string, object> m, string key, long fallback)
    {
        object v;
        if (m != null && m.TryGetValue(key, out v) && v is double) return (long)(double)v;
        return fallback;
    }

    // ---- 빌더 ----

    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // ---- 파서 ----

    static object ParseValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        char c = s[i];
        if (c == '{') return ParseObject(s, ref i);
        if (c == '[') return ParseArray(s, ref i);
        if (c == '"') return ParseString(s, ref i);
        if (c == 't') { i += 4; return true; }
        if (c == 'f') { i += 5; return false; }
        if (c == 'n') { i += 4; return null; }
        return ParseNumber(s, ref i);
    }

    static Dictionary<string, object> ParseObject(string s, ref int i)
    {
        var m = new Dictionary<string, object>();
        i++; // '{'
        SkipWs(s, ref i);
        if (s[i] == '}') { i++; return m; }
        while (true)
        {
            SkipWs(s, ref i);
            string key = ParseString(s, ref i);
            SkipWs(s, ref i);
            i++; // ':'
            m[key] = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; // '}'
            return m;
        }
    }

    static List<object> ParseArray(string s, ref int i)
    {
        var a = new List<object>();
        i++; // '['
        SkipWs(s, ref i);
        if (s[i] == ']') { i++; return a; }
        while (true)
        {
            a.Add(ParseValue(s, ref i));
            SkipWs(s, ref i);
            if (s[i] == ',') { i++; continue; }
            i++; // ']'
            return a;
        }
    }

    static string ParseString(string s, ref int i)
    {
        var sb = new StringBuilder();
        i++; // 여는 따옴표
        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                switch (s[i])
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i + 1, 4), NumberStyles.HexNumber));
                        i += 4;
                        break;
                    default: sb.Append(s[i]); break;
                }
            }
            else sb.Append(s[i]);
            i++;
        }
        i++; // 닫는 따옴표
        return sb.ToString();
    }

    static object ParseNumber(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && "-+.eE0123456789".IndexOf(s[i]) >= 0) i++;
        return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
    }
}
