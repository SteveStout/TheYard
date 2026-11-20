using System.Net;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The site's one closing row, as /about draws it (ADR: The sales pages set the site's look, the addendum on the one
/// close): a card with the question "What has to work, where is it today, and what does success look like?", its line,
/// and three moves, Book 20 minutes (the counted redirect to the booking email), the gold Resume (PDF) and Message me on
/// LinkedIn, with one quiet link under them, TheYard on GitHub. The React pages draw the same row from
/// src/lib/reviewVerdict.ts (AskClose.tsx); the page is not React, so the words are held here and AskCloseHtmlTests
/// holds them equal to the React ones. Its look is written in the page's own sheet with the site's token values, as the
/// rest of that sheet is.
/// </summary>
public static class AskCloseHtml
{
    /// <summary>The closing question, the three the booking email asks.</summary>
    public const string Question = "What has to work, where is it today, and what does success look like?";

    /// <summary>The line under the question.</summary>
    public const string Line = "Book 20 minutes opens an email to me with those three questions in it, and I answer with a time.";

    /// <summary>The filled move's words.</summary>
    public const string Book = "Book 20 minutes";

    /// <summary>The gold move's words.</summary>
    public const string Resume = "Resume (PDF)";

    /// <summary>The outlined move's words.</summary>
    public const string LinkedIn = "Message me on LinkedIn";

    /// <summary>The quiet link under the moves.</summary>
    public const string GitHub = "TheYard on GitHub";

    /// <summary>Book 20 minutes: the counted redirect to an email asking for a time; no page carries the address.</summary>
    public const string BookAddress = "/api/go/book";

    /// <summary>The resume itself, the address every Resume (PDF) on the site opens.</summary>
    public const string ResumeAddress = "/resume?pdf=1";

    /// <summary>The small PDF page before the resume's words (PdfMark.tsx), its colours the token values.</summary>
    private const string PdfMark =
        "<svg viewBox=\"0 0 20 24\" width=\"20\" height=\"24\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M2 1h11l5 5v16a1 1 0 0 1-1 1H2a1 1 0 0 1-1-1V2a1 1 0 0 1 1-1z\" fill=\"#ffffff\" stroke=\"#7d582e\" stroke-width=\"1.4\"/><path d=\"M13 1v5h5\" fill=\"none\" stroke=\"#7d582e\" stroke-width=\"1.4\"/><rect x=\"3\" y=\"13\" width=\"14\" height=\"6\" rx=\"1\" fill=\"#125568\"/><text x=\"10\" y=\"17.8\" font-size=\"5\" text-anchor=\"middle\" fill=\"#ffffff\" font-weight=\"700\">PDF</text></svg>";

    /// <summary>
    /// The row's look in the page's own sheet: the card (the glass's fill, edge, top rule and shadow), the question beside
    /// its moves, the three pills as pills.css draws them, and on a phone the moves under the question at the row's width,
    /// each a thumb tall.
    /// </summary>
    public const string Style = """
          .op-ask-card { margin-top: 20px; padding: 20px 24px; background: rgba(255, 255, 255, 0.88); border: 1px solid rgba(16, 73, 90, 0.18); border-top: 3px solid #10495a; border-radius: 10px; box-shadow: 0 10px 30px rgba(16, 73, 90, 0.12), inset 0 1px 0 rgba(255, 255, 255, 0.9), inset 0 0 0 1px rgba(255, 255, 255, 0.55); }
          .op-ask { display: grid; grid-template-columns: minmax(0, 1.3fr) minmax(0, 1fr); align-items: center; gap: 24px; }
          .op-ask-q { margin: 0 0 8px; color: #10495a; font-size: 24px; font-weight: 600; line-height: 1.3; letter-spacing: -0.01em; }
          .op-ask-sub { margin: 0; color: #524b48; font-size: 14px; }
          .op-ask-moves { display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end; justify-self: end; gap: 12px; max-width: 540px; }
          .op-pill-primary, .op-pill-secondary { display: inline-flex; align-items: center; justify-content: center; min-height: 40px; padding: 0 20px; border: 1px solid #12677f; border-radius: 999px; font: inherit; font-size: 15px; font-weight: 500; text-decoration: none; white-space: nowrap; }
          .op-pill-primary { background: #12677f; color: #ffffff; }
          .op-pill-primary:hover { background: #0f576b; }
          .op-pill-secondary { background: #ffffff; color: #12677f; }
          .op-pill-secondary:hover { background: #ddebee; }
          .op-pill-resume { display: inline-flex; align-items: center; justify-content: center; gap: 10px; box-sizing: border-box; min-height: 48px; padding: 0 24px 0 16px; border: 1px solid #7d582e; border-radius: 999px; background: #b8923f linear-gradient(180deg, #f3e2a6 0%, #e6cb7e 38%, #c9a95c 70%, #b8923f 100%); box-shadow: 0 1px 0 rgba(255, 255, 255, 0.7) inset, 0 6px 16px rgba(125, 88, 46, 0.28); color: #2f2410; font: inherit; font-size: 17px; font-weight: 600; line-height: 1.2; text-decoration: none; white-space: nowrap; }
          .op-pill-resume:hover { filter: brightness(1.04); }
          .op-pill-resume svg { flex: none; }
          .op-ask-moves a:focus-visible { outline: 2px solid #12677f; outline-offset: 2px; }
          .ask-quiet { margin: 12px 0 0; }
          @media (max-width: 639.98px) { .op-ask-card { padding: 16px; } .op-ask { grid-template-columns: minmax(0, 1fr); gap: 16px; } .op-ask-q { font-size: 20px; } .op-ask-moves { justify-self: stretch; max-width: none; } .op-ask-moves a { flex: 1 1 100%; } .op-ask-moves .op-pill-primary, .op-ask-moves .op-pill-secondary { min-height: 44px; } .op-pill-resume { padding: 0 20px 0 16px; } .ask-quiet a { display: inline-flex; align-items: center; min-height: 44px; } }
        """;

    /// <summary>The row, with LinkedIn at <paramref name="linkedIn"/> and the quiet link at <paramref name="gitHub"/>.</summary>
    /// <param name="linkedIn">Steven's LinkedIn page, the first of the page's profiles.</param>
    /// <param name="gitHub">TheYard on GitHub, the second.</param>
    public static string Html(string linkedIn, string gitHub)
    {
        string e(string value) => WebUtility.HtmlEncode(value);
        return $"""
            <section class="op-ask-card" aria-labelledby="ask-close-title" data-mock="close" data-testid="ask-close">
            <div class="op-ask" data-mock="close-ask"><div><h2 id="ask-close-title" class="op-ask-q" data-mock="close-question">{e(Question)}</h2><p class="op-ask-sub">{e(Line)}</p></div>
            <div class="op-ask-moves" data-mock="close-moves"><a class="op-pill-primary" href="{e(BookAddress)}">{e(Book)}</a><a class="op-pill-resume" href="{e(ResumeAddress)}">{PdfMark}{e(Resume)}</a><a class="op-pill-secondary" href="{e(linkedIn)}" rel="me">{e(LinkedIn)}</a></div></div>
            <p class="ask-quiet"><a href="{e(gitHub)}" rel="me">{e(GitHub)}</a></p>
            </section>
            """;
    }
}
