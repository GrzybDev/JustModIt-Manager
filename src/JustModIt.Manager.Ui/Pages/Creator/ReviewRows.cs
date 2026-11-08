using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A row of a list that the check keeps while its item stays, so a control in it keeps its focus.</summary>
public interface IReviewRow
{
    /// <summary>What the row stands for, the same across plans.</summary>
    string Key { get; }
}

/// <summary>A problem or warning about an operation or step, which the author can accept when the rules allow it.</summary>
/// <param name="Text">What it is about, as a sentence.</param>
/// <param name="Tone">Its colour.</param>
/// <param name="AcknowledgementKey">The key that accepts it, and takes it back once accepted; null for one that cannot be accepted.</param>
/// <param name="IsAccepted">The author accepted it, which the line says in place of the way to accept it, with the way back.</param>
public sealed record FindingLine(string Text, Tone Tone, string? AcknowledgementKey = null, bool IsAccepted = false)
{
    public bool CanAcknowledge => AcknowledgementKey is not null && !IsAccepted;

    public Geometry Icon => Tone switch
    {
        Tone.Danger => Theme.Icons.CircleX,
        Tone.Success => Theme.Icons.CircleCheck,
        _ => Theme.Icons.TriangleAlert,
    };
}

/// <summary>Brings the shown rows in line with the rows of a new plan, keeping each instance that stays, and turns their values for the views.</summary>
internal static class ReviewRows
{
    public static readonly IValueConverter IsDanger = new FuncValueConverter<Tone, bool>(tone => tone == Tone.Danger);

    public static readonly IValueConverter IsWarning = new FuncValueConverter<Tone, bool>(tone => tone == Tone.Warning);

    public static readonly IValueConverter IsSuccess = new FuncValueConverter<Tone, bool>(tone => tone == Tone.Success);

    /// <summary>A delete step has one path, which takes the room of both.</summary>
    public static readonly IValueConverter SpanWhenAlone = new FuncValueConverter<bool, int>(hasSecond => hasSecond ? 1 : 3);

    /// <summary>Keeps the rows of a plan for the next one, under their keys.</summary>
    /// <param name="known">The rows shown before, by key; replaced with the rows given.</param>
    /// <param name="rows">The rows of the plan.</param>
    /// <typeparam name="TRow">The rows' type.</typeparam>
    public static void Remember<TRow>(Dictionary<string, TRow> known, IEnumerable<TRow> rows)
        where TRow : IReviewRow
    {
        known.Clear();
        foreach (var row in rows)
        {
            known[row.Key] = row;
        }
    }
}

/// <summary>Numbers repeated keys, so each row of a list has one of its own, e.g. two runs of new files in one folder.</summary>
internal sealed class RowKeys
{
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    /// <summary>The key itself the first time, then with how many times it came.</summary>
    /// <param name="key">What the row stands for.</param>
    public string Next(string key)
    {
        var count = _seen.GetValueOrDefault(key) + 1;
        _seen[key] = count;
        return count == 1 ? key : key + "#" + count.ToString(CultureInfo.InvariantCulture);
    }
}
