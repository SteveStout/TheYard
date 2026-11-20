import { describe, expect, it, vi } from 'vitest';
import { plainClick, scrollToSection, type ClickLike } from './sectionScroll';

const click = (overrides: Partial<ClickLike> = {}): ClickLike & { prevented: boolean } => {
  const event = {
    metaKey: false,
    ctrlKey: false,
    shiftKey: false,
    altKey: false,
    button: 0,
    prevented: false,
    preventDefault: () => {
      event.prevented = true;
    },
    ...overrides,
  };
  return event;
};

const page = (reduced: boolean, has = true) => {
  const scrollIntoView = vi.fn();
  const doc = {
    getElementById: (id: string) => (has && id === 'the-work' ? { scrollIntoView } : null),
    defaultView: { matchMedia: () => ({ matches: reduced }) },
  } as unknown as Document;
  return { doc, scrollIntoView };
};

describe('a door to a section of the same page (sales1120)', () => {
  it('scrolls on a plain click and keeps the address, smoothly unless the reader asked for less motion', () => {
    const smooth = page(false);
    const event = click();
    expect(scrollToSection(event, 'the-work', smooth.doc)).toBe(true);
    expect(event.prevented).toBe(true);
    expect(smooth.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' });
    const still = page(true);
    scrollToSection(click(), 'the-work', still.doc);
    expect(still.scrollIntoView).toHaveBeenCalledWith({ behavior: 'auto', block: 'start' });
  });

  it('leaves a modified click and a section the page has not drawn to the browser', () => {
    const { doc, scrollIntoView } = page(false);
    const modified = click({ metaKey: true });
    expect(scrollToSection(modified, 'the-work', doc)).toBe(false);
    expect(modified.prevented).toBe(false);
    const missing = click();
    expect(scrollToSection(missing, 'the-work', page(false, false).doc)).toBe(false);
    expect(missing.prevented).toBe(false);
    expect(scrollIntoView).not.toHaveBeenCalled();
    expect(plainClick(click({ button: 1 }))).toBe(false);
    expect(plainClick(click())).toBe(true);
  });
});
