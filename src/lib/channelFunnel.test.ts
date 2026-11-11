import { describe, expect, it } from 'vitest';
import { channelRows, replyRate, type ChannelsWeek } from './channelFunnel';

const cell = (channel: string, touches = 0, replies = 0, rooms = 0) => ({
  channel,
  touches,
  replies,
  rooms,
});

describe('the funnel by channel card', () => {
  it('prints a reply rate as whole per cent of touches, blank with none', () => {
    expect([replyRate(0, 0), replyRate(40, 2), replyRate(3, 1), replyRate(0, 2)]).toEqual([
      '',
      '5 %',
      '33 %',
      '',
    ]);
  });

  it('keeps each week newest first, only the channels that moved, and one row for a quiet week', () => {
    const weeks: ChannelsWeek[] = [
      {
        week: '2026-11-09',
        channels: [
          cell('pounce', 1),
          cell('hotmgr', 12, 1),
          cell('email campaign'),
          cell('recruiters', 0, 2, 1),
        ],
      },
      { week: '2026-11-02', channels: [cell('pounce'), cell('hotmgr')] },
    ];
    expect(channelRows({ weeks }).map((row) => row.key)).toEqual([
      '2026-11-09 pounce',
      '2026-11-09 hotmgr',
      '2026-11-09 recruiters',
      '2026-11-02 pounce',
    ]);
    expect(channelRows({ weeks })[1].replyRate).toBe('8 %');
  });
});
