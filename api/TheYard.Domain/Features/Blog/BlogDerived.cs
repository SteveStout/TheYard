// What a save works out from a post and keeps beside it (ADR: Every LinkedIn post is also a blog post, the addendum
// on the read model): the words, the keywords and the derived parts together. Domain's own records, beside
// PostDerivation, the rule that builds them, because Domain cannot see the Application ring the blog's other records
// sit in (ADR-088, the addendum on Data holding the rules' records).
namespace TheYard.Domain.Features.Blog;

/// <summary>One word of a post as the search keeps it: the word or two neighbouring words, where, and how often.</summary>
/// <param name="Word">Lower case; two words have one space between.</param>
/// <param name="Field">1 the title, 2 the summary, 3 the body.</param>
/// <param name="Mentions">How many times.</param>
public sealed record BlogWord(string Word, byte Field, int Mentions);

/// <summary>One keyword a post really talks about, and how many times it names it.</summary>
/// <param name="Key">The keyword's key.</param>
/// <param name="Mentions">How many times.</param>
public sealed record BlogKeywordCount(string Key, int Mentions);

/// <summary>What a save works out from a post, kept beside it: the body drawn once, the minutes, the words and the keywords.</summary>
/// <param name="BodyHtml">The body as HTML.</param>
/// <param name="ReadingMinutes">The body's words over 230, rounded up.</param>
/// <param name="Words">The words the search reads.</param>
/// <param name="Keywords">The keywords the chips read.</param>
/// <param name="Version">The rules' version it was worked out with.</param>
public sealed record BlogDerived(string BodyHtml, int ReadingMinutes, IReadOnlyList<BlogWord> Words, IReadOnlyList<BlogKeywordCount> Keywords, int Version);
