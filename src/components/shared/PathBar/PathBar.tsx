/**
 * Does:      Draws the review route's numbered path (ADR: The sales pages set the site's look), as the sales1120 picture
 *            draws it. At a desk: a bar fixed to the top of the window, from the collapsed rail's edge to the right
 *            edge, on once the first move has left the window and off when it returns, with the six steps (the one the
 *            reader is in marked current, the ones before it done, only the current one's name under 1100 px), then the
 *            gold resume and Book 20 minutes. On a phone: no bar, a thin row over the dock with six marks and
 *            "02 of 06, Three questions", on and off with the dock. A step scrolls to its section and leaves the
 *            address as the reader had it, its view and any r= code with it.
 * Does not:  Hold the steps or decide which is current (pathSteps.ts does), open the booking sheet (the page does, on
 *            Book) or draw its look (sales.css, op-pathbar and op-pathdock, does). Loaded after the first screen.
 * Used by:   ReviewPage.tsx.
 */
import { useEffect, useState } from 'react';
import { currentStep, dockLine, PATH_LABEL, PATH_STEPS, stepState } from '../../../lib/pathSteps';
import type { ReviewAction } from '../../../lib/reviewProgress';
import { BOOK_ADDRESS, RESUME_ADDRESS, VERDICT } from '../../../lib/reviewVerdict';
import { scrollToSection } from '../../../lib/sectionScroll';
import { PdfMark } from '../PdfMark';

export function PathBar({
  watch,
  hidden,
  onAction,
  onBook,
}: {
  /** The selector of the first screen's first move: the path is on once it has left the window. */
  watch: string;
  /** The booking sheet is open: the phone's row goes with the dock. */
  hidden: boolean;
  onAction: (action: ReviewAction) => void;
  onBook: () => void;
}) {
  const [place, setPlace] = useState({ on: false, current: -1 });
  useEffect(() => {
    let frame = 0;
    const read = () => {
      frame = 0;
      const first = document.querySelector(watch);
      const on = first !== null && first.getBoundingClientRect().bottom < 0;
      const tops = PATH_STEPS.map(
        (step) => document.getElementById(step.id)?.getBoundingClientRect().top ?? null
      );
      const foot = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2;
      const current = currentStep(tops, window.innerHeight, foot);
      setPlace((last) => (last.on === on && last.current === current ? last : { on, current }));
    };
    const soon = () => {
      if (frame === 0) frame = window.requestAnimationFrame(read);
    };
    read();
    window.addEventListener('scroll', soon, { passive: true });
    window.addEventListener('resize', soon);
    return () => {
      window.removeEventListener('scroll', soon);
      window.removeEventListener('resize', soon);
      if (frame !== 0) window.cancelAnimationFrame(frame);
    };
  }, [watch]);

  const { on, current } = place;
  return (
    <>
      <nav
        className={`op-pathbar${on ? ' is-on' : ''}`}
        aria-label={PATH_LABEL}
        inert={!on}
        data-mock="pathbar"
        data-testid="review-pathbar"
      >
        <ol>
          {PATH_STEPS.map((step, index) => (
            <li key={step.id}>
              <a
                href={`#${step.id}`}
                className={stepState(index, current) === 'done' ? 'is-done' : undefined}
                aria-current={index === current ? 'true' : 'false'}
                onClick={(event) => scrollToSection(event, step.id)}
                data-mock="path-step"
              >
                <b>{step.number}</b>
                <span>{step.name}</span>
              </a>
            </li>
          ))}
        </ol>
        <div className="op-pathbar-moves">
          <a
            className="op-pill-resume"
            href={RESUME_ADDRESS}
            onClick={() => onAction('resume')}
            data-action="review.resume"
            data-mock="path-resume"
          >
            <PdfMark />
            {VERDICT.resume}
          </a>
          <a
            className="op-pill-primary"
            href={BOOK_ADDRESS}
            onClick={() => {
              onAction('book');
              onBook();
            }}
            data-action="review.book"
          >
            {VERDICT.book}
          </a>
        </div>
      </nav>
      {!hidden && (
        <div
          className={`op-pathdock${on ? ' is-on' : ''}`}
          aria-hidden="true"
          data-mock="pathdock"
          data-testid="review-pathdock"
        >
          {PATH_STEPS.map((step, index) => (
            <i key={step.id} className={`is-${stepState(index, current) || 'next'}`} />
          ))}
          <b>{dockLine(current)}</b>
        </div>
      )}
    </>
  );
}
