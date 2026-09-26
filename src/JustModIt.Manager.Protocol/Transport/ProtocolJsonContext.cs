using System.Text.Json.Serialization;

namespace JustModIt.Manager.Protocol.Transport;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Frame))]
internal sealed partial class ProtocolJsonContext : JsonSerializerContext;
