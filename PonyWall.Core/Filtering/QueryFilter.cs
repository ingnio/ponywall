using System;
using System.Collections.Generic;
using System.Text;

namespace pylorak.TinyWall.Filtering
{
    /// <summary>
    /// Google-style boolean query parser + matcher for the app's text filter
    /// boxes (Connections, Settings > Application Exceptions, History).
    ///
    /// Supported operators:
    ///
    ///   <c>bare</c>          — row matches if any haystack contains the term.
    ///                          Multiple bare terms are implicit-AND'd.
    ///
    ///   <c>-term</c>         — row is excluded if any haystack contains the term.
    ///                          Negation binds to a single atom, not to an OR group.
    ///
    ///   <c>"exact phrase"</c> — substring match on the literal phrase (spaces included).
    ///                          Works with negation: <c>-"exact phrase"</c>.
    ///
    ///   <c>AND</c>, <c>OR</c> — only recognized when literally all-caps. Anything else
    ///                          (<c>and</c>, <c>And</c>, embedded in a longer token) is
    ///                          treated as a regular search term.
    ///
    ///   Precedence: OR binds tighter than AND (Google convention), so
    ///   <c>chrome OR firefox dns</c> means <c>(chrome OR firefox) AND dns</c>.
    ///
    /// All matching is case-insensitive via <c>ToUpperInvariant</c>. The parser is
    /// forgiving: dangling AND/OR with nothing on one side is ignored rather than
    /// erroring, and an empty query matches everything (so a blank filter box
    /// shows all rows, same as the old behavior).
    ///
    /// Thread-safety: an instance is immutable after <see cref="Parse"/> returns,
    /// so the same parsed filter can be shared across threads.
    /// </summary>
    public sealed class QueryFilter
    {
        private readonly List<OrGroup> _groups;

        private QueryFilter(List<OrGroup> groups)
        {
            _groups = groups;
        }

        /// <summary>An empty filter that matches every row. Handy for a null-object return.</summary>
        public static QueryFilter Empty { get; } = new QueryFilter(new List<OrGroup>());

        /// <summary>True if this filter has no operative terms and should match every row.</summary>
        public bool IsEmpty => _groups.Count == 0;

        /// <summary>
        /// Parses a raw query string into a <see cref="QueryFilter"/>. Never returns null —
        /// an empty / whitespace / null input produces <see cref="Empty"/>.
        /// </summary>
        public static QueryFilter Parse(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Empty;

            var tokens = Tokenize(input);
            var groups = new List<OrGroup>();
            OrGroup? current = null;
            bool expectingOrContinuation = false;

            foreach (var tok in tokens)
            {
                if (tok.Kind == TokenKind.And)
                {
                    // Commit the current group and start fresh on next atom.
                    if (current != null && current.Atoms.Count > 0)
                    {
                        groups.Add(current);
                        current = null;
                    }
                    expectingOrContinuation = false;
                    continue;
                }
                if (tok.Kind == TokenKind.Or)
                {
                    // Next atom should join the currently-building group
                    // rather than starting a new one.
                    expectingOrContinuation = true;
                    continue;
                }

                // Regular atom (possibly quoted, possibly prefixed with '-').
                var atom = ParseSignedAtom(tok.Text);
                if (atom.Text.Length == 0)
                    continue; // lone '-' or empty quotes — skip

                if (expectingOrContinuation && current != null)
                {
                    current.Atoms.Add(atom);
                    expectingOrContinuation = false;
                }
                else
                {
                    if (current != null && current.Atoms.Count > 0)
                        groups.Add(current);
                    current = new OrGroup();
                    current.Atoms.Add(atom);
                }
            }

            if (current != null && current.Atoms.Count > 0)
                groups.Add(current);

            return groups.Count == 0 ? Empty : new QueryFilter(groups);
        }

        /// <summary>
        /// Returns true if the row whose searchable fields are the given strings matches this filter.
        /// Null or empty haystacks are ignored. An <see cref="IsEmpty"/> filter always returns true.
        /// </summary>
        public bool Matches(params string?[] haystacks)
        {
            if (_groups.Count == 0)
                return true;
            if (haystacks == null)
                return false;

            // Upper-case each non-empty haystack exactly once, reused across all groups.
            var uppered = new List<string>(haystacks.Length);
            for (int i = 0; i < haystacks.Length; i++)
            {
                var h = haystacks[i];
                if (!string.IsNullOrEmpty(h))
                    uppered.Add(h!.ToUpperInvariant());
            }

            for (int i = 0; i < _groups.Count; i++)
            {
                if (!_groups[i].Matches(uppered))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// If this query is a single positive bare-or-phrase term (no negation, no OR, no
        /// extra AND groups), returns its literal text so callers can push it into a
        /// SQL LIKE / Contains fast path. Returns false otherwise, meaning the caller
        /// must fetch a superset and apply <see cref="Matches"/> client-side.
        ///
        /// Used by HistoryWindow to decide whether the SQL-side
        /// <c>WHERE col LIKE '%…%'</c> clause still covers the whole query, or whether
        /// the filter has boolean operators that the SQL layer can't express.
        /// </summary>
        public bool TryGetSimpleLikePattern(out string pattern)
        {
            pattern = string.Empty;
            if (_groups.Count != 1)
                return false;
            var g = _groups[0];
            if (g.Atoms.Count != 1)
                return false;
            var a = g.Atoms[0];
            if (a.Negated)
                return false;
            pattern = a.Text;
            return true;
        }

        // =================================================================
        // Parser internals
        // =================================================================

        private enum TokenKind { Term, And, Or }

        private readonly struct Token
        {
            public readonly TokenKind Kind;
            public readonly string Text;
            public Token(TokenKind kind, string text) { Kind = kind; Text = text; }
        }

        private static List<Token> Tokenize(string input)
        {
            var result = new List<Token>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '"')
                {
                    // Keep the quote chars in the token so ParseSignedAtom
                    // can recognize and strip them. This lets us distinguish
                    // an unquoted ``AND`` (operator) from a quoted ``"AND"``
                    // (literal term).
                    sb.Append(c);
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        FlushToken(sb, result);
                        sb.Clear();
                    }
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                FlushToken(sb, result);

            return result;
        }

        private static void FlushToken(StringBuilder sb, List<Token> result)
        {
            var raw = sb.ToString();
            // Recognize AND / OR only when they are exactly the all-caps
            // token with no surrounding quotes, no negation prefix. Anything
            // else — "and", "And", "-AND", "\"AND\"" — falls through as a
            // literal search term.
            if (raw.Length == 3 && raw[0] == 'A' && raw[1] == 'N' && raw[2] == 'D')
                result.Add(new Token(TokenKind.And, raw));
            else if (raw.Length == 2 && raw[0] == 'O' && raw[1] == 'R')
                result.Add(new Token(TokenKind.Or, raw));
            else
                result.Add(new Token(TokenKind.Term, raw));
        }

        private static Atom ParseSignedAtom(string raw)
        {
            // Lone dash — no term to negate, treat as empty so the caller skips it.
            if (raw == "-")
                return new Atom(string.Empty, false);

            bool negated = false;
            if (raw.Length >= 2 && raw[0] == '-')
            {
                negated = true;
                raw = raw.Substring(1);
            }

            // Strip surrounding quotes if the token is a phrase.
            if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                raw = raw.Substring(1, raw.Length - 2);

            return new Atom(raw.ToUpperInvariant(), negated);
        }

        // =================================================================
        // Predicate tree: flat conjunction of OR groups, each containing
        // one or more signed atoms. Matching a row means ALL groups match;
        // a group matches if ANY of its atoms matches.
        // =================================================================

        private sealed class OrGroup
        {
            public List<Atom> Atoms { get; } = new List<Atom>();

            public bool Matches(List<string> upperedHaystacks)
            {
                for (int i = 0; i < Atoms.Count; i++)
                {
                    if (Atoms[i].Matches(upperedHaystacks))
                        return true;
                }
                return false;
            }
        }

        private readonly struct Atom
        {
            public readonly string Text;
            public readonly bool Negated;

            public Atom(string text, bool negated)
            {
                Text = text;
                Negated = negated;
            }

            public bool Matches(List<string> upperedHaystacks)
            {
                if (Text.Length == 0)
                    return !Negated; // empty positive atom is a no-op match; empty negation is also a no-op

                bool found = false;
                for (int i = 0; i < upperedHaystacks.Count; i++)
                {
                    if (upperedHaystacks[i].IndexOf(Text, StringComparison.Ordinal) >= 0)
                    {
                        found = true;
                        break;
                    }
                }
                return Negated ? !found : found;
            }
        }
    }
}
