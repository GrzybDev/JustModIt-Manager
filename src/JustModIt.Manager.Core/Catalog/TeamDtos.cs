using System.Text.Json;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>The team behind a project, as project lists name it.</summary>
public sealed record TeamReference
{
    public required Guid Uuid { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    public string Avatar { get; init => field = value ?? ""; } = "";
}

/// <summary>A team as the teams list and project pages show it; only project pages fill its links and donations.</summary>
public sealed record Team
{
    public required Guid Uuid { get; init; }

    public required string Slug { get; init; }

    public required string Name { get; init; }

    public string Summary { get; init => field = value ?? ""; } = "";

    public string Avatar { get; init => field = value ?? ""; } = "";

    public int MemberCount { get; init; }

    public bool HasPublicProject { get; init; }

    public string DonationMessage { get; init => field = value ?? ""; } = "";

    public IReadOnlyList<TeamDonationMethod> DonationMethods { get; init => field = value ?? []; } = [];

    public IReadOnlyList<TeamLink> Links { get; init => field = value ?? []; } = [];

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The team as a project names it.</summary>
    public TeamReference ToReference() => new() { Uuid = Uuid, Slug = Slug, Name = Name, Avatar = Avatar };
}

public sealed record LinkPlatform
{
    public required Guid Uuid { get; init; }

    public required string Name { get; init; }

    public string Icon { get; init => field = value ?? ""; } = "";

    public string Color { get; init => field = value ?? ""; } = "";

    /// <summary>For donation methods: the value is an address rather than, say, an account number.</summary>
    public bool IsLink { get; init; }

    public int Position { get; init; }
}

public sealed record TeamLink
{
    public required Guid Uuid { get; init; }

    public LinkPlatform? Platform { get; init; }

    public string Label { get; init => field = value ?? ""; } = "";

    public string Url { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }
}

public sealed record TeamDonationMethod
{
    public required Guid Uuid { get; init; }

    public LinkPlatform? Platform { get; init; }

    public string Label { get; init => field = value ?? ""; } = "";

    public string Value { get; init => field = value ?? ""; } = "";

    public JsonElement Extra { get; init; }

    public int Position { get; init; }
}
