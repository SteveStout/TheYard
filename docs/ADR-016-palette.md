# ADR: The palette

Status: accepted, 2026-09-02, shipped as 1.0.0.21.

## Context

The site had shipped in a navy and electric blue it inherited from the
take-home it grew out of, and by evening Steve had chosen its own: color
scheme 7 in Figma's website color schemes library, "Urban slate", five
colors in gray, brown and blue. Figma's line on it: "this color scheme
pulls from foggy cityscapes to evoke a sense of calm and sophistication.
The combination of light and dark shades creates a sense of depth and
contrast, while the overall color scheme maintains a serene and
professional aesthetic." Steve's instruction, in full: "Use this one. Make
it all match."

The five, as Figma prints them: #E9E6E7 (a light warm gray), #5E5653 (a
dark brown-gray), #7B7F8A (a slate gray), #AB978C (a warm taupe) and
#6B7C98 (a slate blue).

## Decision

Every color token in the site comes from the five, and where a shade had
to move for readability, the record beside it says which of the five it
came from and why.

- **The ground is the light gray.** The page behind the cards is #E9E6E7
  as printed; cards and the sidebar are white so the depth Figma describes
  is real. Borders are the same gray a step darker.
- **Text is the brown-gray.** Body text is #5E5653 as printed; it measures
  7.2:1 on white and 5.8:1 on the ground, past the 4.5:1 that WCAG AA asks
  of normal text. Headings and the brand use a deeper cut of the same
  brown-gray, #3F3A37, for weight.
- **Muted text is the slate gray, deepened.** #7B7F8A as printed measures
  4.0:1 on white, short of AA for small text, so muted labels use #62666F
  (5.8:1 on white, 4.6:1 on the ground) and the faintest labels #6F737E
  (4.7:1 on white). The printed slate gray survives as the icon color in
  the sidebar, where 3:1 is the bar and it clears it.
- **Actions are the slate blue, deepened.** White text on #6B7C98 as
  printed measures 4.2:1, so buttons and links use #536786 (5.7:1 with
  white text on it, 5.7:1 as link text on white). A pale tint of the
  blue, #E4E9F1, sits behind hover and current rows, and the focus ring
  and the current row's edge are the deepened blue.
- **The taupe is the brand mark.** #AB978C measures 2.8:1 on white, which
  rules it out for text; it marks the lightning bolt in the sidebar and
  the header, and the favicon, the only warm note on the page, where
  contrast rules do not apply to a decoration.
- **Status colors stay semantic.** Green, amber and red on titles,
  condition grades and the live badge carry meaning a buyer reads at a
  glance, and they are the one part of the page deliberately outside the
  five. Shadows and dimmed backdrops are the brown-gray at low opacity.

## In the code

The palette, read from this build
([`src/styles/tokens.css`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.css)):

```live path=src/styles/tokens.css region=palette
```

The sidebar's share of it:

```live path=src/styles/tokens.css region=sheet-tokens
```

The proof is
[`src/styles/tokens.test.ts`](https://github.com/SteveStout/TheYard/blob/main/src/styles/tokens.test.ts),
which reads the tokens file and holds every text and ground pair above to
WCAG AA, so a shade that fails contrast fails the build.

## Consequences

- One file changes the look of everything, because every component reads
  the tokens and none carries a color of its own. The repaint touched the
  tokens, two dimmed backdrops, two placeholder gradients, the countdown
  chip on photos, and the favicon.
- Three of the five needed a deeper cut to clear AA for text. The printed
  values still appear where they can: the ground, the body text, the
  sidebar icons, the hover tint, the brand mark.
- A future palette is the same exercise: replace the values in the region
  above, run the unit test, ship.
