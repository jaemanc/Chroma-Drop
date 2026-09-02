// MiniJson.cs — 최소 JSON 파서. UnityEngine 비의존이라 콘솔 툴에서도 쓴다.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ChromaDrop.Engine
{
    /// <summary>최소 JSON 파서 (콘솔 검증 전용).</summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            try { return Value(s, ref i); }
            catch (Exception) { return null; }
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            return Number(s, ref i);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var m = new Dictionary<string, object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (s[i] == '}') { i++; return m; }
                string k = Str(s, ref i);
                Ws(s, ref i); i++;              // ':'
                m[k] = Value(s, ref i);
                Ws(s, ref i);
                if (s[i] == ',') i++;
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            while (true)
            {
                Ws(s, ref i);
                if (s[i] == ']') { i++; return a; }
                a.Add(Value(s, ref i));
                Ws(s, ref i);
                if (s[i] == ',') i++;
            }
        }

        static string Str(string s, ref int i)
        {
            i++;
            var sb = new System.Text.StringBuilder();
            while (s[i] != '"')
            {
                if (s[i] == '\\') { i++; sb.Append(s[i]); }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }

        static object Number(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }
    }

}
