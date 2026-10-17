using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace JustModIt.Manager.Tests;

/// <summary>Checks that what a source-generated JSON context writes, it can read back.</summary>
internal static class JsonContracts
{
    /// <summary>
    /// Lists, as Type.member, the members the context may leave out of what it writes but requires when it reads, since one
    /// such member makes the whole document unreadable.
    /// </summary>
    /// <param name="context">The context whose types are walked, from its serializable roots.</param>
    public static IEnumerable<string> RequiredButLeftOut(JsonSerializerContext context)
    {
        var seen = new HashSet<Type>();
        var pending = new Queue<Type>(context.GetType().GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType == typeof(JsonSerializableAttribute))
            .Select(attribute => (Type)attribute.ConstructorArguments[0].Value!));
        while (pending.TryDequeue(out var type))
        {
            if (!seen.Add(type) || context.GetTypeInfo(type) is not { } info)
            {
                continue;
            }

            foreach (var derived in info.PolymorphismOptions?.DerivedTypes ?? [])
            {
                pending.Enqueue(derived.DerivedType);
            }

            foreach (var inner in new[] { info.ElementType, info.KeyType, Nullable.GetUnderlyingType(type) }.OfType<Type>())
            {
                pending.Enqueue(inner);
            }

            foreach (var property in info.Properties)
            {
                pending.Enqueue(property.PropertyType);
                if (IsRequired(property, context.Options) && IsLeftOut(property, context.Options))
                {
                    yield return $"{type.Name}.{property.Name}";
                }
            }
        }
    }

    private static bool IsRequired(JsonPropertyInfo property, JsonSerializerOptions options) =>
        property.IsRequired
        || (options.RespectRequiredConstructorParameters
            && property.AssociatedParameter is { HasDefaultValue: false, IsMemberInitializer: false });

    private static bool IsLeftOut(JsonPropertyInfo property, JsonSerializerOptions options)
    {
        var condition = property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: false)
            .OfType<JsonIgnoreAttribute>().FirstOrDefault()?.Condition ?? options.DefaultIgnoreCondition;
        return condition switch
        {
            JsonIgnoreCondition.WhenWriting or JsonIgnoreCondition.WhenWritingDefault => true,
            // A member declared not to hold null is the type system's to keep filled.
            JsonIgnoreCondition.WhenWritingNull => property.AssociatedParameter?.IsNullable ?? property.IsGetNullable,
            _ => false,
        };
    }
}
