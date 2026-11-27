import { describe, expect, it } from 'vitest';
import { ABOUT_ORDER, placed, ROW_ORDER } from './aboutOrder';

// The About page's order is Steve's ruling (professional woven between the personal, not clustered at the top) as the
// about design draws it, top to bottom (J:\mentor\look1127\design\design-about.html).
describe("the About page's order", () => {
  it('is the design, top to bottom, with the photographs alternating left and right', () => {
    expect(
      ABOUT_ORDER.map((entry) => ('row' in entry ? `${entry.row}:${entry.side}` : entry.part))
    ).toEqual([
      'head',
      'lead',
      'St. Charles:left',
      'work',
      'History:right',
      'Food:left',
      'voices',
      'Games:right',
      'Modding:left',
      'voices-more',
      'coaching',
      'Too many house plants:right',
      'Freya and Vincent:wide',
      'write',
      'strip',
      'close',
    ]);
  });

  it('draws every row the document has: a new one after the last named row, its side taking its turn', () => {
    const order = placed([...ROW_ORDER, 'Gardening']);
    const at = order.findIndex((entry) => 'row' in entry && entry.row === 'Gardening');
    expect(order[at - 1]).toEqual({ row: 'Freya and Vincent', side: 'wide' });
    expect(order[at]).toEqual({ row: 'Gardening', side: 'left' });
    expect(order[at + 1]).toEqual({ part: 'write' });
  });

  it('leaves out a named row the document no longer has, and nothing else', () => {
    const order = placed(ROW_ORDER.filter((title) => title !== 'Food'));
    expect(order).toHaveLength(ABOUT_ORDER.length - 1);
    expect(order).not.toContainEqual({ row: 'Food', side: 'left' });
  });
});
