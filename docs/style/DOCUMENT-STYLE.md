# How the documents are styled

Status: written, 2026-10-06, shipped as 1.0.3.85.

**Every page in this library is plain Markdown in Git, and {{live:live-blocks}} code samples across {{live:live-documents}} documents are read from the running build when a page opens.**

A reader on GitHub sees an ordinary Markdown file. A reader on this site sees the same file drawn in the site's own look: glass panels, a status reading, sections with addresses, and code that cannot drift from the code it shows. Nothing in the Markdown asks for any of it. This page uses each feature as it explains it, so what you are looking at is the example.

## In plain words

The documents are written as plain text files kept with the code. When the site shows one, it turns each section into a panel, gives each section a link of its own, and fills every code sample from the version of the code that is running right now.

What that is worth: a developer writes documentation the way they write a README and gets a styled page for free, and the organization never ships a page whose code samples are out of date, because the samples are read from the build instead of pasted.

## Every second-level heading is a panel

The renderer turns the Markdown into HTML, and one function splits that HTML at every second-level heading. The title and any words before the first heading open the page as a lead panel. Each heading after that starts a frosted-glass panel that runs to the next heading. The writer adds no markup, and a page with no second-level heading is one panel.

```live path=src/lib/docLayout.ts region=layout-document
```

The panel is the site's one shared glass (`op-glass`), the same surface the cards and tiles on every other page use, so a document can never drift from the rest of the look (ADR: The glass look).

## The status line is a reading

Most decision records open with a sentence like the one at the top of this page: "Status: written, 2026-10-06, shipped as 1.0.3.85." That sentence is a reading, so it is drawn as one: a label and a value for each part, in small spaced capitals. Whatever follows the first sentence stays a paragraph, word for word, and a status line in any other shape is left as the writer wrote it.

```live path=src/lib/docLayout.ts region=status-reading
```

## Every section has an address

Each second-level heading gets an id made from its words, the way GitHub makes one, so a link can land on a section. This section's address is [`?doc=document-style#every-section-has-an-address`](https://theyard.stevenstout.biz/?doc=document-style#every-section-has-an-address). Two headings with the same words are told apart by a number, as on GitHub. The id comes from the words alone, so it is the same on every visit and on both domains.

```live path=src/lib/markdown.ts region=heading-ids
```

## Code samples are read from the running build

A code sample in these documents is never pasted. The writer marks a region in the source with a pair of `#region` and `#endregion` comments, and the document asks for that region by file and name with an empty fence whose opening line names it, such as `live path=src/lib/markdown.ts region=heading-ids` after the three backticks. When the page is requested, the server replaces the fence with the lines between the two markers as they are in the build that is serving the page, and adds a caption naming the file, the region and the commit. The sample above is one of them, and so is this one, the code that does the replacing:

```live path=api/TheYard.Api/LiveSamples.cs region=expander
```

A fence whose file or region is missing would render a note in place of the code, so the gate fails on it: `LiveSampleCoverageTests` expands every document the catalogue serves and fails if any block comes back as a note, and checks the Dockerfile copies every file a sample reads (ADR: Live code samples).

Numbers work the same way. The count of code samples at the top of this page is a placeholder the server fills in when the page is requested, so it is right on the day it is read, never on the day it was typed.

```readouts
Live code samples | {{live:live-blocks}}
Documents that carry one | {{live:live-documents}}
Documents served | {{live:documents}}
Design tokens | {{live:design-tokens}}
```

## Fences that are not code

A fence's name decides what it becomes. Most names are a language, and the block is highlighted as code. A few names draw part of the site instead, and their contents are data:

| Fence | What it draws |
| --- | --- |
| `swatches` | Colour swatches read from the design token sheet, each with its contrast figures |
| `tiles` | A tile per page, in the landing page's look |
| `readouts` | Stat tiles, with numbers the server counted |
| `glossary` | Terms, their CSS, and a link to where each is used |
| `ribbon-strip` | The live ribbons from the page's background |

This is a `swatches` fence of three design tokens. The colours and the figures come from the design token sheet when the page is drawn, so the fence holds only names:

```swatches
--color-accent | Teal, the accent
--color-green-dark | Dark green
--gradient-gold | The gold bar
```

The readouts in the section above are a `readouts` fence. The renderer that chooses between code and these fences is one function:

```live path=src/lib/markdown.ts region=code-renderer
```

## Highlighting, tables, pictures and links

- **Highlighting.** Code goes through highlight.js with only the grammars the documents use, so a reader downloads the languages this library is written in and no others (ADR: Code that reads like code).
- **Tables.** Every table sits in a scroller of its own that the keyboard can reach, so a phone scrolls a wide table instead of breaking a word. The table above is one.
- **Pictures.** A picture loads when the reader reaches it, and its width and height travel in its address, so the page holds its room and nothing below it moves when it arrives.
- **Links.** A link that leaves the site opens a new tab. A link to this site is made relative, so a copy running on a laptop opens its own pages and not the live ones.

The renderer and every one of these rules is [`src/lib/markdown.ts`](https://github.com/SteveStout/TheYard/blob/main/src/lib/markdown.ts), and it is loaded only when a reader opens a document, so the inventory page never pays for it.

## The writing rule

Every document opens with an "In plain words" section: what it says in two or three sentences a reader outside the field can follow, then what that is worth. The one page without it is About Steven, which is a person and not a document about the system. On a record it is the first panel after the lead, so a reader who stops there has the point.

## Try it in another repository

1. Keep the documents as Markdown in the repository, next to the code, and write them for GitHub first.
2. Render them with a Markdown library (this site uses marked), then split the HTML at every second-level heading and wrap each part in a panel.
3. Give every second-level heading an id from its words, the way GitHub does, so links land on sections.
4. Mark the code you want to show with region comments, and have the server fill a named fence from those lines at request time, with the commit in the caption.
5. Add a test that expands every document and fails on any sample it cannot fill.

The fifth step is what keeps the documents honest. Without it, a renamed region turns into a missing sample on the live site while every check stays green.

## The decisions behind it

- [ADR-014, Live code samples](https://theyard.stevenstout.biz/?doc=adr-live-samples)
- [ADR-019, The React configuration, explained for a new developer](https://theyard.stevenstout.biz/?doc=adr-react)
- [ADR-074, Code that reads like code](https://theyard.stevenstout.biz/?doc=adr-highlighting)
- [ADR-081, The glass look](https://theyard.stevenstout.biz/?doc=adr-glass-look)
- [ADR-016, The palette](https://theyard.stevenstout.biz/?doc=adr-palette)
