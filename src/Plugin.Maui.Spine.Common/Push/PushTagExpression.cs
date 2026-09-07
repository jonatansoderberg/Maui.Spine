namespace Plugin.Maui.Spine.Common;

/// <summary>
/// A boolean expression over an installation's tags, in the syntax Azure Notification Hubs uses:
/// tags combined with <c>&amp;&amp;</c>, <c>||</c> and <c>!</c>, grouped with parentheses. Unlike
/// the hub there is no limit on the number of tags in an expression.
/// </summary>
/// <remarks>
/// Tags are opaque strings compared with ordinal equality, so <c>user:121330</c> and
/// <c>User:121330</c> are different tags. A tag is any run of characters that is not whitespace and
/// not one of <c>&amp; | ! ( )</c>.
/// </remarks>
/// <example>
/// <code>
/// var expression = PushTagExpression.Parse("kind:results-published &amp;&amp; competition:59691");
/// expression.Matches(installation.Tags);
/// </code>
/// </example>
public sealed class PushTagExpression
{
    private readonly Node _root;

    private PushTagExpression(Node root)
    {
        _root = root;
        var tags = new HashSet<string>(StringComparer.Ordinal);
        root.CollectTags(tags);
        ReferencedTags = tags;
    }

    /// <summary>Every tag the expression names, in no particular order.</summary>
    /// <remarks>A register can use this to narrow the candidates before evaluating.</remarks>
    public IReadOnlySet<string> ReferencedTags { get; }

    /// <summary>An expression that matches every installation.</summary>
    public static PushTagExpression MatchAll { get; } = new(TrueNode.Instance);

    /// <summary>Parses <paramref name="expression"/>.</summary>
    /// <param name="expression">The expression source.</param>
    /// <returns>The parsed expression.</returns>
    /// <exception cref="FormatException">The expression is not valid; the message says where.</exception>
    public static PushTagExpression Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return TryParse(expression, out var parsed, out var error)
            ? parsed!
            : throw new FormatException(error);
    }

    /// <summary>Parses <paramref name="expression"/> without throwing.</summary>
    /// <param name="expression">The expression source.</param>
    /// <param name="result">The parsed expression, or <see langword="null"/> when it is not valid.</param>
    /// <param name="error">What is wrong and where, or <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when <paramref name="expression"/> parsed.</returns>
    public static bool TryParse(string expression, out PushTagExpression? result, out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "The expression is empty.";
            return false;
        }

        var tokens = Tokenize(expression, out error);
        if (tokens is null) return false;

        var parser = new Parser(tokens);
        var root = parser.ParseExpression(out error);
        if (root is null) return false;

        if (!parser.AtEnd)
        {
            error = $"Unexpected '{parser.Current.Text}' at position {parser.Current.Position}.";
            return false;
        }

        result = new PushTagExpression(root);
        return true;
    }

    /// <summary>Whether an installation carrying <paramref name="tags"/> matches.</summary>
    /// <param name="tags">The installation's tags.</param>
    /// <returns><see langword="true"/> when the expression holds.</returns>
    public bool Matches(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var set = tags as IReadOnlySet<string> ?? new HashSet<string>(tags, StringComparer.Ordinal);
        return _root.Evaluate(set);
    }

    /// <summary>The expression in normalized form, fully parenthesized where precedence matters.</summary>
    public override string ToString() => _root.ToString()!;

    // --- tokens ---

    private enum TokenKind { Tag, And, Or, Not, Open, Close }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static List<Token>? Tokenize(string source, out string? error)
    {
        error = null;
        var tokens = new List<Token>();

        for (var i = 0; i < source.Length;)
        {
            var c = source[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            switch (c)
            {
                case '(': tokens.Add(new(TokenKind.Open, "(", i)); i++; continue;
                case ')': tokens.Add(new(TokenKind.Close, ")", i)); i++; continue;
                case '!': tokens.Add(new(TokenKind.Not, "!", i)); i++; continue;
                case '&' or '|':
                {
                    if (i + 1 >= source.Length || source[i + 1] != c)
                    {
                        error = $"Single '{c}' at position {i}; use '{c}{c}'.";
                        return null;
                    }
                    tokens.Add(new(c == '&' ? TokenKind.And : TokenKind.Or, $"{c}{c}", i));
                    i += 2;
                    continue;
                }
            }

            var start = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && !IsOperatorChar(source[i])) i++;
            tokens.Add(new(TokenKind.Tag, source[start..i], start));
        }

        if (tokens.Count == 0)
        {
            error = "The expression is empty.";
            return null;
        }

        return tokens;

        static bool IsOperatorChar(char c) => c is '&' or '|' or '!' or '(' or ')';
    }

    // --- parser: or := and ('||' and)*, and := unary ('&&' unary)*, unary := '!' unary | primary ---

    private sealed class Parser(List<Token> tokens)
    {
        private int _index;

        internal bool AtEnd => _index >= tokens.Count;
        internal Token Current => tokens[_index];

        internal Node? ParseExpression(out string? error) => ParseOr(out error);

        private Node? ParseOr(out string? error)
        {
            var left = ParseAnd(out error);
            if (left is null) return null;

            while (!AtEnd && Current.Kind == TokenKind.Or)
            {
                _index++;
                var right = ParseAnd(out error);
                if (right is null) return null;
                left = new OrNode(left, right);
            }

            return left;
        }

        private Node? ParseAnd(out string? error)
        {
            var left = ParseUnary(out error);
            if (left is null) return null;

            while (!AtEnd && Current.Kind == TokenKind.And)
            {
                _index++;
                var right = ParseUnary(out error);
                if (right is null) return null;
                left = new AndNode(left, right);
            }

            return left;
        }

        private Node? ParseUnary(out string? error)
        {
            error = null;

            if (AtEnd)
            {
                error = $"The expression ends after '{tokens[^1].Text}'.";
                return null;
            }

            var token = Current;
            switch (token.Kind)
            {
                case TokenKind.Not:
                    _index++;
                    var operand = ParseUnary(out error);
                    return operand is null ? null : new NotNode(operand);

                case TokenKind.Open:
                {
                    _index++;
                    var inner = ParseOr(out error);
                    if (inner is null) return null;
                    if (AtEnd || Current.Kind != TokenKind.Close)
                    {
                        error = $"Unclosed '(' at position {token.Position}.";
                        return null;
                    }
                    _index++;
                    return new GroupNode(inner);
                }

                case TokenKind.Tag:
                    _index++;
                    return new TagNode(token.Text);

                default:
                    error = $"Expected a tag at position {token.Position}, found '{token.Text}'.";
                    return null;
            }
        }
    }

    // --- tree ---

    private abstract class Node
    {
        internal abstract bool Evaluate(IReadOnlySet<string> tags);
        internal abstract void CollectTags(HashSet<string> into);
    }

    private sealed class TrueNode : Node
    {
        internal static readonly TrueNode Instance = new();
        internal override bool Evaluate(IReadOnlySet<string> tags) => true;
        internal override void CollectTags(HashSet<string> into) { }
        public override string ToString() => "*";
    }

    private sealed class TagNode(string tag) : Node
    {
        internal override bool Evaluate(IReadOnlySet<string> tags) => tags.Contains(tag);
        internal override void CollectTags(HashSet<string> into) => into.Add(tag);
        public override string ToString() => tag;
    }

    private sealed class NotNode(Node operand) : Node
    {
        internal override bool Evaluate(IReadOnlySet<string> tags) => !operand.Evaluate(tags);
        internal override void CollectTags(HashSet<string> into) => operand.CollectTags(into);
        public override string ToString() => $"!{operand}";
    }

    private sealed class AndNode(Node left, Node right) : Node
    {
        internal override bool Evaluate(IReadOnlySet<string> tags) =>
            left.Evaluate(tags) && right.Evaluate(tags);

        internal override void CollectTags(HashSet<string> into)
        {
            left.CollectTags(into);
            right.CollectTags(into);
        }

        public override string ToString() => $"{left} && {right}";
    }

    private sealed class OrNode(Node left, Node right) : Node
    {
        internal override bool Evaluate(IReadOnlySet<string> tags) =>
            left.Evaluate(tags) || right.Evaluate(tags);

        internal override void CollectTags(HashSet<string> into)
        {
            left.CollectTags(into);
            right.CollectTags(into);
        }

        public override string ToString() => $"{left} || {right}";
    }

    private sealed class GroupNode(Node inner) : Node
    {
        internal override bool Evaluate(IReadOnlySet<string> tags) => inner.Evaluate(tags);
        internal override void CollectTags(HashSet<string> into) => inner.CollectTags(into);
        public override string ToString() => $"({inner})";
    }
}
