using TestEngineer.Core.Models;

namespace TestEngineer.Adb.Parsing;

/// <summary>解析 <c>adb devices -l</c>（以及不带 -l 的 <c>serial\tstate</c>）输出。</summary>
public static class DevicesListParser
{
    private const string NoPermissions = "no permissions";

    public static IReadOnlyList<AndroidDevice> Parse(string? output)
    {
        var devices = new List<AndroidDevice>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return devices;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var device = ParseLine(rawLine);
            if (device is not null)
            {
                devices.Add(device);
            }
        }

        return devices;
    }

    public static AndroidDevice? ParseLine(string? rawLine)
    {
        var line = rawLine?.Trim();
        if (string.IsNullOrEmpty(line)
            || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith('*')
            || line.StartsWith("adb server", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var serialEnd = IndexOfWhitespace(line);
        if (serialEnd <= 0)
        {
            return null;
        }

        var serial = line[..serialEnd];
        var rest = line[serialEnd..].Trim();
        if (rest.Length == 0)
        {
            return null;
        }

        string rawState;
        string attributes;
        if (rest.StartsWith(NoPermissions, StringComparison.OrdinalIgnoreCase))
        {
            // 形如 "no permissions (user in plugdev group; ...); see [http://...]"，其后通常还有 usb:/transport_id: 字段。
            rawState = NoPermissions;
            var seeIndex = rest.IndexOf("]", StringComparison.Ordinal);
            attributes = seeIndex >= 0 ? rest[(seeIndex + 1)..] : string.Empty;
        }
        else
        {
            var stateEnd = IndexOfWhitespace(rest);
            rawState = stateEnd < 0 ? rest : rest[..stateEnd];
            attributes = stateEnd < 0 ? string.Empty : rest[stateEnd..];
        }

        string? model = null, product = null, deviceName = null, transportId = null, usb = null;
        foreach (var token in attributes.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = token.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var key = token[..colon];
            var value = token[(colon + 1)..];
            switch (key)
            {
                case "model":
                    model = value;
                    break;
                case "product":
                    product = value;
                    break;
                case "device":
                    deviceName = value;
                    break;
                case "transport_id":
                    transportId = value;
                    break;
                case "usb":
                    usb = value;
                    break;
            }
        }

        return new AndroidDevice(serial, AndroidDevice.ParseState(rawState), rawState, model, product, deviceName, transportId, usb);
    }

    private static int IndexOfWhitespace(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
