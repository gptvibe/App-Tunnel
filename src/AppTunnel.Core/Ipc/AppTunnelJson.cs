using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppTunnel.Core.Ipc;

public static class AppTunnelJson
{
    public static JsonSerializerOptions Default { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}