package app.xingshu.installer;

import java.io.IOException;
import java.util.ArrayList;
import java.util.Collection;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;

/** Bounded pure-Java RFC 8259 JSON parser and serializer safe for both Android and JVM tests. */
final class MiniJson {
    private static final int MAX_CHARS = 32 * 1024 * 1024;
    private static final int MAX_DEPTH = 64;

    private final String src;
    private int pos;
    private final int len;

    private MiniJson(String src) {
        this.src = src;
        this.len = src.length();
    }

    static Object parse(String text) throws IOException {
        if (text == null || text.length() > MAX_CHARS) {
            throw new IOException("JSON 内容过大或为空");
        }
        String clean = !text.isEmpty() && text.charAt(0) == '\uFEFF' ? text.substring(1) : text;
        MiniJson p = new MiniJson(clean);
        p.skipWs();
        Object value = p.readValue(0);
        p.skipWs();
        if (p.pos != p.len) {
            throw new IOException("JSON 末尾存在多余内容");
        }
        return value;
    }

    @SuppressWarnings("unchecked")
    static Map<String, Object> parseObject(String text) throws IOException {
        Object v = parse(text);
        if (!(v instanceof Map<?, ?>)) {
            throw new IOException("期望 JSON 对象");
        }
        return (Map<String, Object>) v;
    }

    /**
     * Parses a large root JSON object while skipping unwanted key values in-place without
     * allocating maps/lists/strings for fields the caller does not inspect.
     */
    static Map<String, Object> parseObjectFields(String text, Set<String> wantedKeys) throws IOException {
        if (text == null || text.length() > MAX_CHARS) {
            throw new IOException("JSON 内容过大或为空");
        }
        String clean = !text.isEmpty() && text.charAt(0) == '\uFEFF' ? text.substring(1) : text;
        MiniJson p = new MiniJson(clean);
        p.skipWs();
        if (p.pos >= p.len || p.src.charAt(p.pos) != '{') {
            throw new IOException("期望 JSON 对象");
        }
        p.pos++;
        Map<String, Object> result = new LinkedHashMap<>();
        p.skipWs();
        if (p.pos < p.len && p.src.charAt(p.pos) == '}') {
            p.pos++;
            p.skipWs();
            if (p.pos != p.len) throw new IOException("JSON 末尾存在多余内容");
            return result;
        }
        while (true) {
            p.skipWs();
            if (p.pos >= p.len || p.src.charAt(p.pos) != '"') {
                throw new IOException("JSON 键格式无效");
            }
            String key = p.readString();
            p.skipWs();
            p.expect(':');
            p.skipWs();
            if (wantedKeys == null || wantedKeys.contains(key)) {
                result.put(key, p.readValue(1));
            } else {
                p.skipValue(1);
            }
            p.skipWs();
            if (p.pos >= p.len) throw new IOException("JSON 对象未正常结束");
            char ch = p.src.charAt(p.pos++);
            if (ch == '}') break;
            if (ch != ',') throw new IOException("JSON 对象缺少逗号");
        }
        p.skipWs();
        if (p.pos != p.len) throw new IOException("JSON 末尾存在多余内容");
        return result;
    }

    static String stringField(Map<String, Object> map, String key) {
        if (map == null || key == null) return "";
        Object val = map.get(key);
        if (val == null) return "";
        return val instanceof String ? (String) val : String.valueOf(val);
    }

    @SuppressWarnings("unchecked")
    static Map<String, Object> objectField(Map<String, Object> map, String key) {
        if (map == null || key == null) return null;
        Object val = map.get(key);
        return val instanceof Map<?, ?> ? (Map<String, Object>) val : null;
    }

    static long longField(Map<String, Object> map, String key, long fallback) {
        if (map == null || key == null) return fallback;
        Object val = map.get(key);
        if (val instanceof Number) return ((Number) val).longValue();
        if (val instanceof String) {
            try { return Long.parseLong((String) val); } catch (NumberFormatException ignored) { }
        }
        return fallback;
    }

    static String stringify(Object value) {
        StringBuilder sb = new StringBuilder();
        writeJson(sb, value);
        return sb.toString();
    }

    private static void writeJson(StringBuilder sb, Object value) {
        if (value == null) {
            sb.append("null");
        } else if (value instanceof Boolean || value instanceof Number) {
            sb.append(value.toString());
        } else if (value instanceof String) {
            writeEscaped(sb, (String) value);
        } else if (value instanceof Map<?, ?>) {
            sb.append('{');
            boolean first = true;
            for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet()) {
                if (!first) sb.append(',');
                first = false;
                writeEscaped(sb, String.valueOf(entry.getKey()));
                sb.append(':');
                writeJson(sb, entry.getValue());
            }
            sb.append('}');
        } else if (value instanceof Collection<?>) {
            sb.append('[');
            boolean first = true;
            for (Object item : (Collection<?>) value) {
                if (!first) sb.append(',');
                first = false;
                writeJson(sb, item);
            }
            sb.append(']');
        } else {
            writeEscaped(sb, String.valueOf(value));
        }
    }

    private static void writeEscaped(StringBuilder sb, String s) {
        sb.append('"');
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            switch (c) {
                case '"': sb.append("\\\""); break;
                case '\\': sb.append("\\\\"); break;
                case '\b': sb.append("\\b"); break;
                case '\f': sb.append("\\f"); break;
                case '\n': sb.append("\\n"); break;
                case '\r': sb.append("\\r"); break;
                case '\t': sb.append("\\t"); break;
                default:
                    if (c < 0x20) sb.append(String.format(java.util.Locale.ROOT, "\\u%04x", (int) c));
                    else sb.append(c);
                    break;
            }
        }
        sb.append('"');
    }

    private Object readValue(int depth) throws IOException {
        if (depth > MAX_DEPTH) throw new IOException("JSON 嵌套过深");
        skipWs();
        if (pos >= len) throw new IOException("JSON 内容提前结束");
        char ch = src.charAt(pos);
        switch (ch) {
            case '{': return readObject(depth + 1);
            case '[': return readArray(depth + 1);
            case '"': return readString();
            case 't': expectLiteral("true"); return Boolean.TRUE;
            case 'f': expectLiteral("false"); return Boolean.FALSE;
            case 'n': expectLiteral("null"); return null;
            default:
                if (ch == '-' || (ch >= '0' && ch <= '9')) return readNumber();
                throw new IOException("JSON 非法字符: " + ch);
        }
    }

    private Map<String, Object> readObject(int depth) throws IOException {
        expect('{');
        Map<String, Object> map = new LinkedHashMap<>();
        skipWs();
        if (pos < len && src.charAt(pos) == '}') {
            pos++;
            return map;
        }
        while (true) {
            skipWs();
            if (pos >= len || src.charAt(pos) != '"') throw new IOException("JSON 键格式无效");
            String key = readString();
            skipWs();
            expect(':');
            Object val = readValue(depth);
            map.put(key, val);
            skipWs();
            if (pos >= len) throw new IOException("JSON 对象未正常结束");
            char ch = src.charAt(pos++);
            if (ch == '}') break;
            if (ch != ',') throw new IOException("JSON 对象缺少逗号");
        }
        return map;
    }

    private List<Object> readArray(int depth) throws IOException {
        expect('[');
        List<Object> list = new ArrayList<>();
        skipWs();
        if (pos < len && src.charAt(pos) == ']') {
            pos++;
            return list;
        }
        while (true) {
            list.add(readValue(depth));
            skipWs();
            if (pos >= len) throw new IOException("JSON 数组未正常结束");
            char ch = src.charAt(pos++);
            if (ch == ']') break;
            if (ch != ',') throw new IOException("JSON 数组缺少逗号");
        }
        return list;
    }

    private void skipValue(int depth) throws IOException {
        if (depth > MAX_DEPTH) throw new IOException("JSON 嵌套过深");
        skipWs();
        if (pos >= len) throw new IOException("JSON 内容提前结束");
        char ch = src.charAt(pos);
        switch (ch) {
            case '{':
                pos++;
                skipWs();
                if (pos < len && src.charAt(pos) == '}') { pos++; return; }
                while (true) {
                    skipWs();
                    if (pos >= len || src.charAt(pos) != '"') throw new IOException("JSON 键格式无效");
                    skipString();
                    skipWs();
                    expect(':');
                    skipValue(depth + 1);
                    skipWs();
                    if (pos >= len) throw new IOException("JSON 对象未正常结束");
                    char next = src.charAt(pos++);
                    if (next == '}') break;
                    if (next != ',') throw new IOException("JSON 对象缺少逗号");
                }
                return;
            case '[':
                pos++;
                skipWs();
                if (pos < len && src.charAt(pos) == ']') { pos++; return; }
                while (true) {
                    skipValue(depth + 1);
                    skipWs();
                    if (pos >= len) throw new IOException("JSON 数组未正常结束");
                    char next = src.charAt(pos++);
                    if (next == ']') break;
                    if (next != ',') throw new IOException("JSON 数组缺少逗号");
                }
                return;
            case '"': skipString(); return;
            case 't': expectLiteral("true"); return;
            case 'f': expectLiteral("false"); return;
            case 'n': expectLiteral("null"); return;
            default:
                if (ch == '-' || (ch >= '0' && ch <= '9')) { skipNumber(); return; }
                throw new IOException("JSON 非法字符: " + ch);
        }
    }

    private String readString() throws IOException {
        expect('"');
        int start = pos;
        StringBuilder sb = null;
        while (pos < len) {
            char c = src.charAt(pos++);
            if (c == '"') {
                return sb == null ? src.substring(start, pos - 1) : sb.toString();
            }
            if (c < 0x20) throw new IOException("JSON 字符串含未转义控制字符");
            if (c == '\\') {
                if (sb == null) {
                    sb = new StringBuilder();
                    sb.append(src, start, pos - 1);
                }
                if (pos >= len) throw new IOException("JSON 转义序列未结束");
                char esc = src.charAt(pos++);
                switch (esc) {
                    case '"': sb.append('"'); break;
                    case '\\': sb.append('\\'); break;
                    case '/': sb.append('/'); break;
                    case 'b': sb.append('\b'); break;
                    case 'f': sb.append('\f'); break;
                    case 'n': sb.append('\n'); break;
                    case 'r': sb.append('\r'); break;
                    case 't': sb.append('\t'); break;
                    case 'u':
                        if (pos + 4 > len) throw new IOException("JSON Unicode 转义不完整");
                        int code = Integer.parseInt(src.substring(pos, pos + 4), 16);
                        sb.append((char) code);
                        pos += 4;
                        break;
                    default:
                        throw new IOException("JSON 非法转义: \\" + esc);
                }
            } else if (sb != null) {
                sb.append(c);
            }
        }
        throw new IOException("JSON 字符串未闭合");
    }

    private void skipString() throws IOException {
        expect('"');
        while (pos < len) {
            char c = src.charAt(pos++);
            if (c == '"') return;
            if (c < 0x20) throw new IOException("JSON 字符串含未转义控制字符");
            if (c == '\\') {
                if (pos >= len) throw new IOException("JSON 转义序列未结束");
                char esc = src.charAt(pos++);
                if (esc == 'u') {
                    if (pos + 4 > len) throw new IOException("JSON Unicode 转义不完整");
                    pos += 4;
                }
            }
        }
        throw new IOException("JSON 字符串未闭合");
    }

    private Number readNumber() throws IOException {
        int start = pos;
        skipNumber();
        String raw = src.substring(start, pos);
        if (raw.indexOf('.') >= 0 || raw.indexOf('e') >= 0 || raw.indexOf('E') >= 0) {
            return Double.valueOf(raw);
        }
        try {
            return Long.valueOf(raw);
        } catch (NumberFormatException ignored) {
            return Double.valueOf(raw);
        }
    }

    private void skipNumber() throws IOException {
        if (pos < len && src.charAt(pos) == '-') pos++;
        if (pos >= len) throw new IOException("JSON 数字格式无效");
        if (src.charAt(pos) == '0') {
            pos++;
        } else if (src.charAt(pos) >= '1' && src.charAt(pos) <= '9') {
            while (pos < len && src.charAt(pos) >= '0' && src.charAt(pos) <= '9') pos++;
        } else {
            throw new IOException("JSON 数字格式无效");
        }
        if (pos < len && src.charAt(pos) == '.') {
            pos++;
            if (pos >= len || src.charAt(pos) < '0' || src.charAt(pos) > '9') throw new IOException("JSON 小数格式无效");
            while (pos < len && src.charAt(pos) >= '0' && src.charAt(pos) <= '9') pos++;
        }
        if (pos < len && (src.charAt(pos) == 'e' || src.charAt(pos) == 'E')) {
            pos++;
            if (pos < len && (src.charAt(pos) == '+' || src.charAt(pos) == '-')) pos++;
            if (pos >= len || src.charAt(pos) < '0' || src.charAt(pos) > '9') throw new IOException("JSON 指数格式无效");
            while (pos < len && src.charAt(pos) >= '0' && src.charAt(pos) <= '9') pos++;
        }
    }

    private void expect(char expected) throws IOException {
        if (pos >= len || src.charAt(pos) != expected) {
            throw new IOException("JSON 语法错误，期望 '" + expected + "'");
        }
        pos++;
    }

    private void expectLiteral(String literal) throws IOException {
        if (!src.regionMatches(pos, literal, 0, literal.length())) {
            throw new IOException("JSON 语法错误，期望 " + literal);
        }
        pos += literal.length();
    }

    private void skipWs() {
        while (pos < len) {
            char c = src.charAt(pos);
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') pos++;
            else break;
        }
    }
}
