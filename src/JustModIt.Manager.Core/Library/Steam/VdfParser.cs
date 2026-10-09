using System.Text;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// Reads Valve's text KeyValues (<c>libraryfolders.vdf</c>, <c>appmanifest_*.acf</c>) without reflection into an object
/// holding every top-level key; the binary form (<c>appinfo.vdf</c>) is not supported.
/// </summary>
internal static class VdfParser
{
    private const int MaxDepth = 32;

    /// <summary>Parses KeyValues text.</summary>
    /// <param name="text">The whole text of a file.</param>
    /// <exception cref="VdfFormatException">The text is not well-formed KeyValues.</exception>
    public static VdfNode Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var reader = new Reader(text);
        var document = VdfNode.CreateObject();
        ParseObjectBody(ref reader, document, depth: 0);
        return document;
    }

    /// <summary>Reads and parses a KeyValues file without blocking Steam, which rewrites these files while it runs.</summary>
    /// <param name="fileSystem">Reads the file.</param>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="VdfFormatException">The text is not well-formed KeyValues.</exception>
    /// <exception cref="InvalidDataException">The file is too large to be Steam's.</exception>
    public static async Task<VdfNode> ParseFileAsync(IFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return Parse(await fileSystem.ReadAllTextAsync(path, LauncherJson.MaxFileLength, cancellationToken).ConfigureAwait(false));
    }

    private static void ParseObjectBody(ref Reader reader, VdfNode target, int depth)
    {
        var topLevel = depth == 0;
        while (true)
        {
            var token = reader.Next();
            switch (token.Kind)
            {
                case TokenKind.End when topLevel:
                    return;
                case TokenKind.End:
                    throw new VdfFormatException("Unexpected end of file, missing '}'", reader.Line);
                case TokenKind.CloseBrace when !topLevel:
                    return;
                case TokenKind.CloseBrace:
                    throw new VdfFormatException("Unexpected '}'", reader.Line);
                case TokenKind.OpenBrace:
                    throw new VdfFormatException("Unexpected '{' without a key", reader.Line);
            }

            var key = token.Text!;
            if (topLevel && key.StartsWith('#'))
            {
                // #include and #base pull in other files; nothing Steam writes uses them.
                reader.Next();
                continue;
            }

            var value = reader.Next();
            switch (value.Kind)
            {
                case TokenKind.String:
                    target.Add(key, VdfNode.CreateValue(value.Text!));
                    break;
                case TokenKind.OpenBrace when depth >= MaxDepth:
                    throw new VdfFormatException($"Nesting deeper than {MaxDepth} levels", reader.Line);
                case TokenKind.OpenBrace:
                    var child = VdfNode.CreateObject();
                    ParseObjectBody(ref reader, child, depth + 1);
                    target.Add(key, child);
                    break;
                default:
                    // The key stays out of the message: a damaged file may hold a path there, and messages are logged.
                    throw new VdfFormatException("Missing value for a key", reader.Line);
            }
        }
    }

    private enum TokenKind
    {
        End,
        String,
        OpenBrace,
        CloseBrace,
    }

    private readonly record struct Token(TokenKind Kind, string? Text = null);

    private ref struct Reader(string text)
    {
        private readonly ReadOnlySpan<char> _text = text;
        private int _position;

        public int Line { get; private set; } = 1;

        public Token Next()
        {
            while (true)
            {
                SkipWhitespaceAndComments();
                if (_position >= _text.Length)
                {
                    return new Token(TokenKind.End);
                }

                switch (_text[_position])
                {
                    case '{':
                        _position++;
                        return new Token(TokenKind.OpenBrace);
                    case '}':
                        _position++;
                        return new Token(TokenKind.CloseBrace);
                    case '[':
                        // Platform conditionals such as [$WIN32] matter only to the Source engine; every entry counts.
                        SkipConditional();
                        continue;
                    case '"':
                        return ReadQuoted();
                    default:
                        return ReadUnquoted();
                }
            }
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _text.Length)
            {
                var c = _text[_position];
                if (c == '\n')
                {
                    Line++;
                    _position++;
                }
                else if (char.IsWhiteSpace(c) || c == '\uFEFF')
                {
                    _position++;
                }
                else if (c == '/' && _position + 1 < _text.Length && _text[_position + 1] == '/')
                {
                    var newLine = _text[_position..].IndexOf('\n');
                    _position = newLine < 0 ? _text.Length : _position + newLine;
                }
                else
                {
                    return;
                }
            }
        }

        private void SkipConditional()
        {
            var end = _text[_position..].IndexOf(']');
            if (end < 0 || _text.Slice(_position, end).Contains('\n'))
            {
                throw new VdfFormatException("Unterminated conditional", Line);
            }

            _position += end + 1;
        }

        private Token ReadQuoted()
        {
            _position++;
            var rest = _text[_position..];
            var special = rest.IndexOfAny('"', '\\', '\n');
            if (special >= 0 && rest[special] == '"')
            {
                _position += special + 1;
                return new Token(TokenKind.String, rest[..special].ToString());
            }

            var builder = new StringBuilder();
            while (_position < _text.Length)
            {
                var c = _text[_position++];
                switch (c)
                {
                    case '"':
                        return new Token(TokenKind.String, builder.ToString());
                    case '\\' when _position < _text.Length:
                        AppendEscape(builder, _text[_position++]);
                        break;
                    case '\n':
                        Line++;
                        builder.Append(c);
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            throw new VdfFormatException("Unterminated string", Line);
        }

        private static void AppendEscape(StringBuilder builder, char escaped)
        {
            switch (escaped)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case '\\' or '"':
                    builder.Append(escaped);
                    break;
                default:
                    // Hand-edited files hold unescaped Windows paths ("D:\Games"), which must stay intact.
                    builder.Append('\\').Append(escaped);
                    break;
            }
        }

        private Token ReadUnquoted()
        {
            var start = _position;
            while (_position < _text.Length)
            {
                var c = _text[_position];
                if (char.IsWhiteSpace(c) || c is '{' or '}' or '"')
                {
                    break;
                }

                _position++;
            }

            return new Token(TokenKind.String, _text[start.._position].ToString());
        }
    }
}
