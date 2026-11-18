// What a save works out from a post and keeps beside it: the body drawn to HTML, the reading minutes, the words the
// search reads and the keywords the chips read, stamped with the rules' version. A copy of the rules' answer, made
// in one place, so the store never decides any of it and a change to the rules is a new version to derive again.
using TheYard.Data;

namespace TheYard.Domain.Features.Blog;

/// <summary>Works out a post's derived parts from its title, summary and body.</summary>
public static class PostDerivation
{
    /// <summary>The post's derived parts under the rules' current version.</summary>
    /// <param name="title">The title.</param>
    /// <param name="summary">The summary.</param>
    /// <param name="body">The body as saved.</param>
    public static BlogDerived Of(string title, string summary, string body)
    {
        string plain = BlogMarkdown.Plain(body);
        // Only the pairs a phrase can land on are kept: a typed word never holds a space, so a pair is ever looked
        // for only as one of the synonyms' phrases, and keeping every pair would treble the rows for nothing.
        var words = BlogText.Counted(title, BlogText.Title, KeywordRules.IsPhrasePair)
            .Concat(BlogText.Counted(summary, BlogText.Summary, KeywordRules.IsPhrasePair))
            .Concat(BlogText.Counted(plain, BlogText.Body, KeywordRules.IsPhrasePair))
            .Select(w => new BlogWord(w.Word, w.Field, w.Mentions))
            .ToList();
        var keywords = KeywordRules.KeywordsOf(title, summary, plain).Select(k => new BlogKeywordCount(k.Key, k.Mentions)).ToList();
        return new BlogDerived(BlogBodyHtml.Render(body), BlogText.ReadingMinutes(plain), words, keywords, KeywordRules.Version);
    }
}
