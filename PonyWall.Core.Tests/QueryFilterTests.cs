using pylorak.TinyWall.Filtering;
using Xunit;

namespace pylorak.TinyWall.Tests
{
    public class QueryFilterTests
    {
        // =====================================================================
        // Empty / null / whitespace — match everything (equivalent to no filter)
        // =====================================================================

        [Fact]
        public void Empty_input_matches_everything()
        {
            var f = QueryFilter.Parse("");
            Assert.True(f.IsEmpty);
            Assert.True(f.Matches("anything"));
            Assert.True(f.Matches("", ""));
        }

        [Fact]
        public void Null_input_matches_everything()
        {
            var f = QueryFilter.Parse(null);
            Assert.True(f.IsEmpty);
            Assert.True(f.Matches("chrome.exe"));
        }

        [Fact]
        public void Whitespace_only_input_matches_everything()
        {
            var f = QueryFilter.Parse("   \t  ");
            Assert.True(f.IsEmpty);
            Assert.True(f.Matches("chrome.exe"));
        }

        // =====================================================================
        // Single bare term — substring match across any haystack
        // =====================================================================

        [Fact]
        public void Bare_term_substring_matches_across_haystacks()
        {
            var f = QueryFilter.Parse("chrome");
            Assert.True(f.Matches("chrome.exe", "some path"));
            Assert.True(f.Matches("other", "C:\\Google\\Chrome\\chrome.exe"));
            Assert.False(f.Matches("firefox.exe", "notes.txt"));
        }

        [Fact]
        public void Bare_term_match_is_case_insensitive()
        {
            var f = QueryFilter.Parse("chrome");
            Assert.True(f.Matches("CHROME.EXE"));
            Assert.True(f.Matches("Chrome.exe"));
            var g = QueryFilter.Parse("CHROME");
            Assert.True(g.Matches("chrome.exe"));
        }

        // =====================================================================
        // Implicit AND — multiple bare terms must all match
        // =====================================================================

        [Fact]
        public void Multiple_bare_terms_are_implicit_and()
        {
            var f = QueryFilter.Parse("chrome dns");
            Assert.True(f.Matches("chrome", "dns.service"));
            Assert.True(f.Matches("chrome.exe uses dns"));
            Assert.False(f.Matches("chrome.exe"));       // missing dns
            Assert.False(f.Matches("dnscache"));         // missing chrome
        }

        [Fact]
        public void Implicit_and_matches_across_different_haystacks()
        {
            var f = QueryFilter.Parse("chrome dns");
            // "chrome" matches haystack 1, "dns" matches haystack 2
            Assert.True(f.Matches("chrome.exe", "dns query"));
        }

        // =====================================================================
        // Explicit AND — same as implicit
        // =====================================================================

        [Fact]
        public void Explicit_and_behaves_like_implicit()
        {
            var f = QueryFilter.Parse("chrome AND dns");
            Assert.True(f.Matches("chrome", "dns"));
            Assert.False(f.Matches("chrome"));
        }

        [Fact]
        public void Lowercase_and_is_a_search_term_not_an_operator()
        {
            // "and" (lowercase) is a literal term; implicit-AND'd with "dns"
            var f = QueryFilter.Parse("and dns");
            Assert.True(f.Matches("android dns"));   // contains "and"
            Assert.False(f.Matches("chrome dns"));    // missing "and"
        }

        // =====================================================================
        // OR — binds tighter than AND, matches if ANY atom in the group matches
        // =====================================================================

        [Fact]
        public void Or_matches_either_term()
        {
            var f = QueryFilter.Parse("chrome OR firefox");
            Assert.True(f.Matches("chrome.exe"));
            Assert.True(f.Matches("firefox.exe"));
            Assert.False(f.Matches("edge.exe"));
        }

        [Fact]
        public void Or_binds_tighter_than_implicit_and()
        {
            // "chrome OR firefox dns" = (chrome OR firefox) AND dns
            var f = QueryFilter.Parse("chrome OR firefox dns");
            Assert.True(f.Matches("chrome", "dns"));
            Assert.True(f.Matches("firefox", "dns"));
            Assert.False(f.Matches("chrome"));    // missing dns
            Assert.False(f.Matches("dns"));       // missing both
            Assert.False(f.Matches("edge", "dns"));
        }

        [Fact]
        public void Or_can_chain_more_than_two()
        {
            var f = QueryFilter.Parse("chrome OR firefox OR edge");
            Assert.True(f.Matches("chrome.exe"));
            Assert.True(f.Matches("firefox.exe"));
            Assert.True(f.Matches("edge.exe"));
            Assert.False(f.Matches("brave.exe"));
        }

        [Fact]
        public void Lowercase_or_is_a_search_term_not_an_operator()
        {
            var f = QueryFilter.Parse("chrome or firefox");
            // All three must match (implicit AND) — "or" is a literal
            Assert.True(f.Matches("chrome or firefox.html"));  // contains all 3
            Assert.False(f.Matches("chrome firefox"));          // missing "or"
        }

        // =====================================================================
        // Negation — "-term" excludes rows
        // =====================================================================

        [Fact]
        public void Negation_excludes_rows_containing_the_term()
        {
            var f = QueryFilter.Parse("chrome -svchost");
            Assert.True(f.Matches("chrome.exe"));
            Assert.False(f.Matches("chrome.exe", "svchost.exe"));
            Assert.False(f.Matches("svchost chrome.exe"));  // svchost in same field
        }

        [Fact]
        public void Pure_negation_query_matches_everything_except_the_excluded_term()
        {
            var f = QueryFilter.Parse("-svchost");
            Assert.True(f.Matches("chrome.exe"));
            Assert.True(f.Matches("notepad.exe", "C:\\windows"));
            Assert.False(f.Matches("svchost.exe"));
            Assert.False(f.Matches("C:\\Windows\\System32\\svchost.exe"));
        }

        [Fact]
        public void Multiple_negations_all_apply()
        {
            var f = QueryFilter.Parse("-svchost -system");
            Assert.True(f.Matches("chrome.exe"));
            Assert.False(f.Matches("svchost.exe"));
            Assert.False(f.Matches("System Idle Process"));
        }

        [Fact]
        public void Lone_dash_is_ignored()
        {
            // Bare "-" shouldn't crash the parser or negate nothing
            var f = QueryFilter.Parse("- chrome");
            Assert.True(f.Matches("chrome.exe"));
        }

        // =====================================================================
        // Quoted phrases
        // =====================================================================

        [Fact]
        public void Quoted_phrase_matches_exact_substring_including_spaces()
        {
            var f = QueryFilter.Parse("\"system idle\"");
            Assert.True(f.Matches("System Idle Process"));
            Assert.False(f.Matches("system_idle"));   // underscore breaks the space
            Assert.False(f.Matches("system", "idle")); // split across fields
        }

        [Fact]
        public void Negated_quoted_phrase_excludes()
        {
            var f = QueryFilter.Parse("-\"system idle\"");
            Assert.True(f.Matches("chrome.exe"));
            Assert.False(f.Matches("System Idle Process"));
        }

        [Fact]
        public void Quoted_phrase_with_and_inside_is_a_literal_not_an_operator()
        {
            // "a AND b" as a phrase is one literal token
            var f = QueryFilter.Parse("\"a AND b\"");
            Assert.True(f.Matches("this is a AND b test"));
            Assert.False(f.Matches("a b"));  // must contain the literal "a AND b"
        }

        // =====================================================================
        // TryGetSimpleLikePattern — for HistoryWindow SQL fast path
        // =====================================================================

        [Fact]
        public void Simple_pattern_extracted_for_single_bare_term()
        {
            var f = QueryFilter.Parse("chrome");
            Assert.True(f.TryGetSimpleLikePattern(out var p));
            Assert.Equal("CHROME", p);
        }

        [Fact]
        public void Simple_pattern_extracted_for_single_quoted_phrase()
        {
            var f = QueryFilter.Parse("\"system idle\"");
            Assert.True(f.TryGetSimpleLikePattern(out var p));
            Assert.Equal("SYSTEM IDLE", p);
        }

        [Fact]
        public void No_simple_pattern_when_query_has_negation()
        {
            var f = QueryFilter.Parse("chrome -svchost");
            Assert.False(f.TryGetSimpleLikePattern(out _));
        }

        [Fact]
        public void No_simple_pattern_when_query_has_or()
        {
            var f = QueryFilter.Parse("chrome OR firefox");
            Assert.False(f.TryGetSimpleLikePattern(out _));
        }

        [Fact]
        public void No_simple_pattern_when_query_has_multiple_and_groups()
        {
            var f = QueryFilter.Parse("chrome dns");
            Assert.False(f.TryGetSimpleLikePattern(out _));
        }

        [Fact]
        public void No_simple_pattern_when_filter_is_empty()
        {
            var f = QueryFilter.Parse("");
            Assert.False(f.TryGetSimpleLikePattern(out _));
        }

        // =====================================================================
        // Dangling operators — parser should be forgiving, not throw
        // =====================================================================

        [Fact]
        public void Dangling_and_at_end_is_ignored()
        {
            var f = QueryFilter.Parse("chrome AND");
            Assert.True(f.Matches("chrome.exe"));
            Assert.False(f.Matches("firefox"));
        }

        [Fact]
        public void Dangling_or_at_start_is_ignored()
        {
            var f = QueryFilter.Parse("OR chrome");
            Assert.True(f.Matches("chrome.exe"));
            Assert.False(f.Matches("firefox"));
        }

        // =====================================================================
        // Null haystack handling
        // =====================================================================

        [Fact]
        public void Null_haystack_is_ignored()
        {
            var f = QueryFilter.Parse("chrome");
            Assert.True(f.Matches(null, "chrome.exe"));
            Assert.False(f.Matches(null, "firefox.exe"));
        }

        [Fact]
        public void All_null_haystacks_behave_like_empty()
        {
            var f = QueryFilter.Parse("chrome");
            Assert.False(f.Matches(null, null));
            var g = QueryFilter.Parse("-chrome");
            Assert.True(g.Matches(null, null));  // nothing to exclude, so it passes
        }
    }
}
