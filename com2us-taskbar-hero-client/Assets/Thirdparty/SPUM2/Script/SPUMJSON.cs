using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// 외부 종속 없는 경량 JSON 파서/직렬화기.
/// Dictionary&lt;string, object&gt; = JSON Object, List&lt;object&gt; = JSON Array.
/// </summary>
public static class SPUMJSON
{
    #region Deserialize

    public static object Deserialize(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        var parser = new Parser(json);
        return parser.ParseValue();
    }

    public static Dictionary<string, object> DeserializeObject(string json)
    {
        return Deserialize(json) as Dictionary<string, object>;
    }

    private sealed class Parser
    {
        private readonly string _json;
        private int _pos;

        public Parser(string json) { _json = json; _pos = 0; }

        public object ParseValue()
        {
            SkipWhitespace();
            if (_pos >= _json.Length) return null;

            char c = _json[_pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't':
                case 'f': return ParseBool();
                case 'n': return ParseNull();
                default:  return ParseNumber();
            }
        }

        private Dictionary<string, object> ParseObject()
        {
            var dict = new Dictionary<string, object>();
            _pos++; // skip '{'
            SkipWhitespace();

            if (_pos < _json.Length && _json[_pos] == '}') { _pos++; return dict; }

            while (_pos < _json.Length)
            {
                SkipWhitespace();
                string key = ParseString();
                SkipWhitespace();
                Expect(':');
                object value = ParseValue();
                dict[key] = value;
                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == ',') { _pos++; continue; }
                break;
            }
            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == '}') _pos++;
            return dict;
        }

        private List<object> ParseArray()
        {
            var list = new List<object>();
            _pos++; // skip '['
            SkipWhitespace();

            if (_pos < _json.Length && _json[_pos] == ']') { _pos++; return list; }

            while (_pos < _json.Length)
            {
                list.Add(ParseValue());
                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == ',') { _pos++; continue; }
                break;
            }
            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == ']') _pos++;
            return list;
        }

        private string ParseString()
        {
            Expect('"');
            var sb = new StringBuilder();
            while (_pos < _json.Length)
            {
                char c = _json[_pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (_pos >= _json.Length) break;
                    char esc = _json[_pos++];
                    switch (esc)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 <= _json.Length)
                            {
                                string hex = _json.Substring(_pos, 4);
                                sb.Append((char)int.Parse(hex, NumberStyles.HexNumber));
                                _pos += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private object ParseNumber()
        {
            int start = _pos;
            if (_pos < _json.Length && _json[_pos] == '-') _pos++;
            while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;

            bool isFloat = false;
            if (_pos < _json.Length && _json[_pos] == '.')
            {
                isFloat = true;
                _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
            }
            if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
            {
                isFloat = true;
                _pos++;
                if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
            }

            string numStr = _json.Substring(start, _pos - start);
            if (isFloat)
                return double.Parse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                return l;
            return double.Parse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private bool ParseBool()
        {
            if (_json.Substring(_pos, 4) == "true") { _pos += 4; return true; }
            if (_json.Substring(_pos, 5) == "false") { _pos += 5; return false; }
            throw new FormatException($"Invalid boolean at position {_pos}");
        }

        private object ParseNull()
        {
            if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "null") { _pos += 4; return null; }
            throw new FormatException($"Invalid null at position {_pos}");
        }

        private void SkipWhitespace()
        {
            while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos])) _pos++;
        }

        private void Expect(char c)
        {
            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == c) { _pos++; return; }
            throw new FormatException($"Expected '{c}' at position {_pos}");
        }
    }

    #endregion

    #region Serialize

    public static string Serialize(object obj)
    {
        var sb = new StringBuilder();
        SerializeValue(obj, sb);
        return sb.ToString();
    }

    private static void SerializeValue(object value, StringBuilder sb)
    {
        if (value == null) { sb.Append("null"); return; }

        if (value is string s) { SerializeString(s, sb); return; }
        if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
        if (value is Dictionary<string, object> dict) { SerializeObject(dict, sb); return; }
        if (value is List<object> list) { SerializeArray(list, sb); return; }

        // numbers
        if (value is int i) { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
        if (value is long l) { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
        if (value is float f) { sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (value is double d) { sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return; }

        // fallback: ToString
        SerializeString(value.ToString(), sb);
    }

    private static void SerializeObject(Dictionary<string, object> dict, StringBuilder sb)
    {
        sb.Append('{');
        bool first = true;
        foreach (var kv in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            SerializeString(kv.Key, sb);
            sb.Append(':');
            SerializeValue(kv.Value, sb);
        }
        sb.Append('}');
    }

    private static void SerializeArray(List<object> list, StringBuilder sb)
    {
        sb.Append('[');
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) sb.Append(',');
            SerializeValue(list[i], sb);
        }
        sb.Append(']');
    }

    private static void SerializeString(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b");  break;
                case '\f': sb.Append("\\f");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                default:
                    if (c < ' ')
                        sb.AppendFormat("\\u{0:x4}", (int)c);
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    #endregion

    #region Helpers

    /// <summary>Dictionary에서 string 값을 가져옵니다. 없으면 defaultValue 반환.</summary>
    public static string GetString(Dictionary<string, object> dict, string key, string defaultValue = null)
    {
        if (dict != null && dict.TryGetValue(key, out object val) && val != null)
            return val.ToString();
        return defaultValue;
    }

    /// <summary>Dictionary에서 하위 Dictionary를 가져옵니다.</summary>
    public static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
    {
        if (dict != null && dict.TryGetValue(key, out object val))
            return val as Dictionary<string, object>;
        return null;
    }

    /// <summary>Dictionary에서 하위 List를 가져옵니다.</summary>
    public static List<object> GetArray(Dictionary<string, object> dict, string key)
    {
        if (dict != null && dict.TryGetValue(key, out object val))
            return val as List<object>;
        return null;
    }

    /// <summary>Dictionary에 키가 있는지, 값이 bool인지 확인합니다.</summary>
    public static bool IsBool(Dictionary<string, object> dict, string key)
    {
        return dict != null && dict.TryGetValue(key, out object val) && val is bool;
    }

    #endregion
}
