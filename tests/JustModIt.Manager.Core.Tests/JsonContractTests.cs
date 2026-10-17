using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Tests;

namespace JustModIt.Manager.Core.Tests;

public sealed class JsonContractTests
{
    [Fact]
    public void Every_member_the_engine_leaves_out_of_its_files_is_optional_when_it_reads_them_back()
    {
        JsonSerializerContext[] contexts =
            [InstallationJsonContext.Default, PackageJsonContext.Default, DownloadJsonContext.Default, CatalogueJsonContext.Default];

        contexts.SelectMany(JsonContracts.RequiredButLeftOut).Should().BeEmpty();
    }

    [Fact]
    public void A_member_left_out_on_write_but_required_on_read_is_found_also_inside_a_nullable_struct()
    {
        JsonContracts.RequiredButLeftOut(ExampleJsonContext.Default)
            .Should().BeEquivalentTo("ExampleNote.comment", "ExampleNote.count", "ExampleNote.secret", "ExampleSpan.end");
    }
}

internal sealed record ExampleNote(string Title, string? Comment,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)] string Secret, string? Author = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Flag = false)
{
    public string? Remark { get; init; }

    public ExampleSpan? Span { get; init; }
}

internal readonly record struct ExampleSpan
{
    public required long? End { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ExampleNote))]
internal sealed partial class ExampleJsonContext : JsonSerializerContext;
