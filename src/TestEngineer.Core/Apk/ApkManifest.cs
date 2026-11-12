using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace TestEngineer.Core.Apk;

public sealed record ApkComponent(string Name, bool IsMainEntry, bool IsAlias);

public sealed record ApkManifestReport(
    string PackageName,
    string? SharedUserId,
    bool IsSystemSharedUser,
    IReadOnlyList<ApkComponent> Activities,
    IReadOnlyList<string> Services,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> Permissions)
{
    public const string SystemSharedUserId = "android.uid.system";

    public string ToDisplayText()
    {
        var text = new StringBuilder();
        text.Append("包名：").AppendLine(string.IsNullOrEmpty(PackageName) ? "（未读出）" : PackageName);
        text.Append("界面：").Append(Activities.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个");
        text.Append("服务：").Append(Services.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个");
        text.Append("Provider：").Append(Providers.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个");
        text.Append("权限：").Append(Permissions.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" 个");
        text.Append("android:sharedUserId=\"android.uid.system\"：");
        text.AppendLine(IsSystemSharedUser ? "是" : "否");
        if (!IsSystemSharedUser && !string.IsNullOrEmpty(SharedUserId))
        {
            text.Append("当前 sharedUserId：").AppendLine(SharedUserId);
        }

        text.AppendLine();
        text.AppendLine("主入口");
        var mains = Activities.Where(activity => activity.IsMainEntry).ToList();
        if (mains.Count == 0)
        {
            text.AppendLine("（没有 ACTION_MAIN + CATEGORY_LAUNCHER）");
        }
        else
        {
            foreach (var activity in mains)
            {
                text.AppendLine(activity.IsAlias ? activity.Name + "（别名）" : activity.Name);
            }
        }

        AppendSection(text, "界面", Activities.Select(FormatActivity));
        AppendSection(text, "服务", Services);
        AppendSection(text, "Provider", Providers);
        AppendSection(text, "权限", Permissions);
        return text.ToString().TrimEnd();
    }

    private static string FormatActivity(ApkComponent activity)
    {
        var name = activity.IsAlias ? activity.Name + "（别名）" : activity.Name;
        return activity.IsMainEntry ? "主入口 " + name : name;
    }

    private static void AppendSection(StringBuilder text, string title, IEnumerable<string> lines)
    {
        text.AppendLine();
        text.AppendLine(title);
        var any = false;
        foreach (var line in lines)
        {
            any = true;
            text.AppendLine(line);
        }

        if (!any)
        {
            text.AppendLine("（无）");
        }
    }
}

public sealed class ApkManifestException : Exception
{
    public ApkManifestException(string message)
        : base(message)
    {
    }
}

/// <summary>从 APK 的 AndroidManifest 统计界面、服务、Provider、权限和 sharedUserId。</summary>
public static class ApkManifest
{
    private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private const string MainAction = "android.intent.action.MAIN";
    private const string LauncherCategory = "android.intent.category.LAUNCHER";

    public static ApkManifestReport Read(string apkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apkPath);
        if (!File.Exists(apkPath))
        {
            throw new ApkManifestException("找不到 APK。");
        }

        byte[] bytes;
        try
        {
            using var zip = ZipFile.OpenRead(apkPath);
            var entry = zip.GetEntry("AndroidManifest.xml");
            if (entry is null)
            {
                throw new ApkManifestException("APK 里没有 AndroidManifest.xml。");
            }

            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (ApkManifestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new ApkManifestException("无法读取 APK。\n" + ex.Message);
        }

        return ReadXml(bytes);
    }

    public static ApkManifestReport ReadXml(ReadOnlySpan<byte> xml)
    {
        if (xml.IsEmpty)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var events = LooksLikeText(xml) ? ReadTextEvents(xml) : ReadBinaryEvents(xml);
        return Interpret(events);
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> xml)
    {
        var index = 0;
        if (xml.Length >= 3 && xml[0] == 0xEF && xml[1] == 0xBB && xml[2] == 0xBF)
        {
            index = 3;
        }

        while (index < xml.Length && xml[index] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            index++;
        }

        return index < xml.Length && xml[index] == (byte)'<';
    }

    private static ApkManifestReport Interpret(List<XmlEvt> events)
    {
        var stack = new Stack<Frame>();
        var activities = new List<ApkComponent>();
        var services = new List<string>();
        var providers = new List<string>();
        var permissions = new List<string>();
        var packageName = "";
        string? sharedUserId = null;
        var sawManifest = false;

        foreach (var evt in events)
        {
            if (!evt.IsStart)
            {
                if (stack.Count == 0)
                {
                    throw new ApkManifestException("清单格式无法识别。");
                }

                var closed = stack.Pop();
                if (closed.Kind == FrameKind.Filter && closed.Main && closed.Launcher)
                {
                    foreach (var frame in stack)
                    {
                        if (frame.Kind is FrameKind.Activity or FrameKind.Alias)
                        {
                            frame.IsMainEntry = true;
                            break;
                        }
                    }
                }
                else if (closed.Kind is FrameKind.Activity or FrameKind.Alias)
                {
                    activities.Add(new ApkComponent(closed.Name, closed.IsMainEntry, closed.Kind == FrameKind.Alias));
                }
                else if (closed.Kind == FrameKind.Service)
                {
                    services.Add(closed.Name);
                }
                else if (closed.Kind == FrameKind.Provider)
                {
                    providers.Add(closed.Name);
                }

                continue;
            }

            var attrs = evt.Attributes;
            if (evt.Name == "manifest")
            {
                sawManifest = true;
                if (attrs is not null && attrs.TryGetValue("package", out var package) && package.Length > 0)
                {
                    packageName = package;
                }

                if (sharedUserId is null && attrs is not null && attrs.TryGetValue("sharedUserId", out var uid))
                {
                    sharedUserId = uid;
                }
            }
            else if (evt.Name is "uses-permission" or "uses-permission-sdk-23")
            {
                var permission = attrs is not null && attrs.TryGetValue("name", out var value) ? value : "";
                permissions.Add(permission.Length == 0 ? "（未命名）" : permission);
            }
            else if (evt.Name == "action")
            {
                MarkFilter(stack, attrs, MainAction, launcher: false);
            }
            else if (evt.Name == "category")
            {
                MarkFilter(stack, attrs, LauncherCategory, launcher: true);
            }

            stack.Push(new Frame
            {
                Kind = evt.Name switch
                {
                    "activity" => FrameKind.Activity,
                    "activity-alias" => FrameKind.Alias,
                    "service" => FrameKind.Service,
                    "provider" => FrameKind.Provider,
                    "intent-filter" => FrameKind.Filter,
                    _ => FrameKind.Other,
                },
                Name = Expand(Attribute(attrs, "name"), packageName),
            });
        }

        if (!sawManifest || stack.Count != 0)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var ordered = activities
            .Select((activity, index) => (activity, index))
            .OrderByDescending(item => item.activity.IsMainEntry)
            .ThenBy(item => item.index)
            .Select(item => item.activity)
            .ToList();

        return new ApkManifestReport(
            packageName,
            sharedUserId,
            sharedUserId == ApkManifestReport.SystemSharedUserId,
            ordered,
            services,
            providers,
            permissions);
    }

    private static void MarkFilter(Stack<Frame> stack, Dictionary<string, string>? attrs, string expected, bool launcher)
    {
        if (Attribute(attrs, "name") != expected)
        {
            return;
        }

        foreach (var frame in stack)
        {
            if (frame.Kind != FrameKind.Filter)
            {
                continue;
            }

            if (launcher)
            {
                frame.Launcher = true;
            }
            else
            {
                frame.Main = true;
            }

            break;
        }
    }

    private static string? Attribute(Dictionary<string, string>? attrs, string name)
    {
        if (attrs is null || !attrs.TryGetValue(name, out var value))
        {
            return null;
        }

        return value;
    }

    private static string Expand(string? raw, string packageName)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "（未命名）";
        }

        if (raw.StartsWith('.'))
        {
            return packageName + raw;
        }

        if (!raw.Contains('.') && packageName.Length > 0)
        {
            return packageName + "." + raw;
        }

        return raw;
    }

    private static List<XmlEvt> ReadTextEvents(ReadOnlySpan<byte> xml)
    {
        var events = new List<XmlEvt>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
        };
        using var stream = new MemoryStream(xml.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, settings);
        try
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    var localName = reader.LocalName;
                    var empty = reader.IsEmptyElement;
                    var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (reader.HasAttributes)
                    {
                        for (var i = 0; i < reader.AttributeCount; i++)
                        {
                            reader.MoveToAttribute(i);
                            if (reader.Prefix == "xmlns" || reader.Name == "xmlns")
                            {
                                continue;
                            }

                            var local = reader.LocalName;
                            if (string.IsNullOrEmpty(local))
                            {
                                continue;
                            }

                            if (attrs.ContainsKey(local) && !IsAndroid(reader.NamespaceURI))
                            {
                                continue;
                            }

                            attrs[local] = reader.Value;
                        }

                        reader.MoveToElement();
                    }

                    events.Add(new XmlEvt(true, localName, attrs));
                    if (empty)
                    {
                        events.Add(new XmlEvt(false, localName, null));
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement)
                {
                    events.Add(new XmlEvt(false, reader.LocalName, null));
                }
            }
        }
        catch (XmlException)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        return events;
    }

    private static List<XmlEvt> ReadBinaryEvents(ReadOnlySpan<byte> xml)
    {
        var events = new List<XmlEvt>();
        string[]? pool = null;
        Walk(xml, 0, xml.Length, events, ref pool);
        if (pool is null || events.Count == 0)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        return events;
    }

    private static void Walk(ReadOnlySpan<byte> data, int start, int end, List<XmlEvt> events, ref string[]? pool)
    {
        var pos = start;
        while (pos + 8 <= end)
        {
            var type = U16(data, pos);
            var headerSize = U16(data, pos + 2);
            var sizeRaw = U32(data, pos + 4);
            if (headerSize < 8 || sizeRaw > (uint)(end - pos) || sizeRaw < headerSize)
            {
                throw new ApkManifestException("清单格式无法识别。");
            }

            var size = (int)sizeRaw;

            if (type == 0x0003)
            {
                Walk(data, pos + headerSize, pos + size, events, ref pool);
            }
            else if (type == 0x0001)
            {
                pool = ParseStringPool(data.Slice(pos, size));
            }
            else if (type == 0x0102)
            {
                if (pool is null)
                {
                    throw new ApkManifestException("清单格式无法识别。");
                }

                events.Add(ParseStart(data, pos, headerSize, size, pool));
            }
            else if (type == 0x0103)
            {
                events.Add(new XmlEvt(false, "", null));
            }

            pos += size;
        }
    }

    private static XmlEvt ParseStart(ReadOnlySpan<byte> data, int pos, int headerSize, int size, string[] pool)
    {
        if (headerSize < 16 || pos + headerSize + 20 > pos + size)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var ext = pos + headerSize;
        var name = PoolString(pool, U32(data, ext + 4)) ?? "";
        var attributeStart = U16(data, ext + 8);
        var attributeSize = U16(data, ext + 10);
        var attributeCount = U16(data, ext + 12);
        var attrs = new Dictionary<string, string>(attributeCount, StringComparer.Ordinal);
        if (attributeCount > 0)
        {
            if (attributeSize < 20)
            {
                throw new ApkManifestException("清单格式无法识别。");
            }

            var attrPos = ext + attributeStart;
            var needed = (long)attributeCount * attributeSize;
            if (attrPos < pos || attrPos > pos + size || needed > pos + size - attrPos)
            {
                throw new ApkManifestException("清单格式无法识别。");
            }

            for (var i = 0; i < attributeCount; i++)
            {
                var at = attrPos + (i * attributeSize);
                var ns = PoolString(pool, U32(data, at)) ?? "";
                var local = PoolString(pool, U32(data, at + 4));
                if (string.IsNullOrEmpty(local))
                {
                    continue;
                }

                var value = Resolve(pool, U32(data, at + 8), data[at + 15], U32(data, at + 16));
                if (attrs.ContainsKey(local) && !IsAndroid(ns))
                {
                    continue;
                }

                attrs[local] = value;
            }
        }

        return new XmlEvt(true, name, attrs);
    }

    private static string Resolve(string[] pool, uint raw, byte dataType, uint data)
    {
        if (raw != 0xFFFFFFFF)
        {
            return PoolString(pool, raw) ?? "";
        }

        if (dataType == 0x03)
        {
            return PoolString(pool, data) ?? "";
        }

        if (dataType == 0x12)
        {
            return data != 0 ? "true" : "false";
        }

        if (dataType == 0x10)
        {
            return data.ToString(CultureInfo.InvariantCulture);
        }

        if (dataType == 0x11)
        {
            return "0x" + data.ToString("x", CultureInfo.InvariantCulture);
        }

        return "";
    }

    private static string[] ParseStringPool(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length < 28)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var headerSize = U16(chunk, 2);
        var stringCountRaw = U32(chunk, 8);
        var flags = U32(chunk, 16);
        var stringsStartRaw = U32(chunk, 20);
        if (stringCountRaw > 100_000 || stringsStartRaw > (uint)chunk.Length)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var stringCount = (int)stringCountRaw;
        var stringsStart = (int)stringsStartRaw;
        if (headerSize < 28)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        if ((long)stringCount * 4 > chunk.Length - headerSize)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var utf8 = (flags & 0x100) != 0;
        var result = new string[stringCount];
        for (var i = 0; i < stringCount; i++)
        {
            var offsetRaw = U32(chunk, headerSize + (i * 4));
            if (offsetRaw > int.MaxValue)
            {
                throw new ApkManifestException("清单格式无法识别。");
            }

            var offset = (int)offsetRaw;
            var start = stringsStart + offset;
            if ((uint)start > (uint)chunk.Length)
            {
                throw new ApkManifestException("清单格式无法识别。");
            }

            result[i] = utf8 ? ReadUtf8(chunk, start) : ReadUtf16(chunk, start);
        }

        return result;
    }

    private static string ReadUtf8(ReadOnlySpan<byte> chunk, int start)
    {
        var position = start;
        _ = ReadLen8(chunk, ref position);
        var byteLen = ReadLen8(chunk, ref position);
        if ((uint)position > (uint)chunk.Length || byteLen > chunk.Length - position)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        return Encoding.UTF8.GetString(chunk.Slice(position, byteLen));
    }

    private static int ReadLen8(ReadOnlySpan<byte> chunk, ref int position)
    {
        if ((uint)position >= (uint)chunk.Length)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var first = chunk[position++];
        if ((first & 0x80) == 0)
        {
            return first;
        }

        if ((uint)position >= (uint)chunk.Length)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var second = chunk[position++];
        return ((first & 0x7F) << 8) | second;
    }

    private static string ReadUtf16(ReadOnlySpan<byte> chunk, int start)
    {
        if (start > chunk.Length - 2)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        var charLen = (int)U16(chunk, start);
        var position = start + 2;
        if ((charLen & 0x8000) != 0)
        {
            if (position > chunk.Length - 2)
            {
                throw new ApkManifestException("清单格式无法识别。");
            }

            charLen = ((charLen & 0x7FFF) << 16) | U16(chunk, position);
            position += 2;
        }

        var byteLen = charLen * 2;
        if (byteLen < 0 || position > chunk.Length || byteLen > chunk.Length - position)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        return Encoding.Unicode.GetString(chunk.Slice(position, byteLen));
    }

    private static string? PoolString(string[] pool, uint index)
    {
        if (index == 0xFFFFFFFF)
        {
            return null;
        }

        if (index >= (uint)pool.Length)
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        return pool[index];
    }

    private static bool IsAndroid(string? ns) =>
        string.Equals(ns, AndroidNamespace, StringComparison.Ordinal);

    private static ushort U16(ReadOnlySpan<byte> data, int offset)
    {
        if ((uint)offset > (uint)(data.Length - 2))
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset)
    {
        if ((uint)offset > (uint)(data.Length - 4))
        {
            throw new ApkManifestException("清单格式无法识别。");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
    }

    private readonly record struct XmlEvt(bool IsStart, string Name, Dictionary<string, string>? Attributes);

    private enum FrameKind
    {
        Other,
        Activity,
        Alias,
        Service,
        Provider,
        Filter,
    }

    private sealed class Frame
    {
        public FrameKind Kind { get; init; }

        public string Name { get; init; } = "";

        public bool Main { get; set; }

        public bool Launcher { get; set; }

        public bool IsMainEntry { get; set; }
    }
}
