using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace JustGone.Core
{
    /// <summary>
    /// Minimal zero-dependency JSON reader/writer for net472 / Unity Mono.
    /// Directly reused from WeiXiangQi v0.10.10.0 (shared_p4/WxqMiniJson.cs).
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("null json");
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("trailing json at " + i);
            return v;
        }

        public static bool TryParse(string text, out object value)
        {
            try { value = Parse(text); return true; }
            catch { value = null; return false; }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("object key expected at " + i);
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("colon expected at " + i);
                i++;
                object v = ParseValue(s, ref i);
                d[k] = v;
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("object separator expected at " + i);
            }
            return d;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("array separator expected at " + i);
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("string expected");
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (i >= s.Length) throw new FormatException("escape end");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("unicode escape");
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            i += 4;
                            sb.Append((char)code);
                            break;
                        default: throw new FormatException("bad escape \\" + e);
                    }
                }
                else sb.Append(c);
            }
            throw new FormatException("unterminated string");
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }
            string token = s.Substring(start, i - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException("bad number: " + token);
            return d;
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new FormatException("literal expected at " + i);
            i += literal.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        public static object Get(object root, params string[] path)
        {
            object cur = root;
            for (int i = 0; i < path.Length; i++)
            {
                if (!(cur is Dictionary<string, object> d)) return null;
                if (!d.TryGetValue(path[i], out object next)) return null;
                cur = next;
            }
            return cur;
        }

        public static string GetString(object root, params string[] path) => Get(root, path) as string;

        public static int GetInt(object root, int fallback, params string[] path)
        {
            object o = Get(root, path);
            if (o is double d)
            {
                if (d > int.MaxValue || d < int.MinValue) return fallback;
                return (int)d;
            }
            if (o is string str && int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                return v;
            return fallback;
        }

        public static bool GetBool(object root, bool fallback, params string[] path)
        {
            object o = Get(root, path);
            return o is bool b ? b : fallback;
        }

        public static List<object> GetArray(object root, params string[] path) => Get(root, path) as List<object>;

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string str) { WriteString(sb, str); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is float || value is double || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "0" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is IDictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    Write(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            if (value is IEnumerable en)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Write(sb, o);
                }
                sb.Append(']');
                return;
            }
            throw new ArgumentException("cannot serialize " + value.GetType().FullName);
        }

        public static string Quote(string s)
        {
            var sb = new StringBuilder();
            WriteString(sb, s);
            return sb.ToString();
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
