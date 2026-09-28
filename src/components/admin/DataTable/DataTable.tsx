/**
 * Every table on the Admin tab is drawn by this one component (ADR-083). A
 * card names its columns once. On a wide card the names are the table's
 * headers; on a stacked table they become the label beside each value.
 *
 * A table of three or more columns stacks when its card cannot give every
 * column room (src/lib/tableFit.ts). Each row becomes a block led by its
 * first column, with every other value on its own line behind its column's
 * name. A table of two columns is already a label and a value, so it never
 * stacks.
 *
 * Some browsers (Safari) stop telling a screen reader that a table is a
 * table once its parts are laid out as blocks. So every part carries its
 * ARIA role, and a screen reader hears the same table at every width.
 */
import { type ReactNode, useLayoutEffect, useState } from 'react';
import { STACK_FROM_COLUMNS, stacksAt } from '../../../lib/tableFit';
import styles from '../AdminPanel/AdminPanel.module.css';

export type Column<Row> = {
  /** The header's words, and the label beside each value when a phone stacks the row. */
  name: string;
  /** What the cell holds; the index is the row's place among the rows it is drawn with. */
  cell: (row: Row, index: number) => ReactNode;
  /** The small mono type of an identifier, a time or a reading, which may break at any character. */
  mono?: boolean;
  /** A number: tabular figures, right-aligned in a table and never broken. */
  num?: boolean;
  /**
   * A short reading (a time, a level, a status), kept on one line so a narrow
   * column never breaks it one character per line.
   */
  short?: boolean;
  /** The row's own name, a header for the row rather than a value in it. */
  rowHeader?: boolean;
  /** A class of the card's own for the cell, beside the ones above. */
  className?: string;
  /** A column the reader can sort by: the header is a button, and says whether it is the order. */
  sort?: { active: boolean; onSort: () => void };
};

/** Rows under a heading of their own, a day's rows under the day. */
export type RowGroup<Row> = {
  key: string;
  title: ReactNode;
  testId?: string;
  rows: Row[];
};

type Rows<Row> = { rows: Row[]; groups?: never } | { groups: RowGroup<Row>[]; rows?: never };

export type DataTableProps<Row> = Rows<Row> & {
  /** The name a screen reader gives the scrolling region round the table. */
  label: string;
  columns: Column<Row>[];
  rowKey: (row: Row, index: number) => string | number;
  testId?: string;
  /** A class and a test id for a row, where a row carries a state or a test finds it. */
  rowProps?: (row: Row) => { className?: string; testId?: string };
  /** A line across the table where there are no rows; none leaves the body empty. */
  empty?: ReactNode;
  /** A line across the table after the rows, a note on all of them. */
  note?: { content: ReactNode; testId?: string };
};

const joined = (...names: (string | false | undefined)[]) =>
  names.filter((name) => typeof name === 'string' && name !== '').join(' ') || undefined;

export function DataTable<Row>(props: DataTableProps<Row>) {
  const { label, columns, rowKey, testId, rowProps, empty, note } = props;
  const across = columns.length;
  const narrow = columns.filter((column) => column.num === true || column.short === true).length;
  // The box is measured before paint and on every resize, so the first frame a
  // reader sees is already the right shape.
  const [box, setBox] = useState<HTMLDivElement | null>(null);
  const [stacks, setStacks] = useState(false);
  useLayoutEffect(() => {
    if (box === null || across < STACK_FROM_COLUMNS) return;
    const measure = () => setStacks(stacksAt(box.clientWidth, across - narrow, narrow));
    measure();
    const watcher = new ResizeObserver(measure);
    watcher.observe(box);
    return () => watcher.disconnect();
  }, [box, across, narrow]);

  const row = (entry: Row, index: number) => {
    const own = rowProps?.(entry);
    return (
      <tr
        role="row"
        key={rowKey(entry, index)}
        className={own?.className}
        data-testid={own?.testId}
      >
        {columns.map((column, at) => {
          const className = joined(
            column.mono === true && styles.mono,
            column.num === true && styles.num,
            column.short === true && styles.short,
            at === 0 && stacks && styles.lead,
            column.className
          );
          // The first column leads a stacked row and needs no label; the rest name themselves.
          const label = at === 0 ? undefined : column.name;
          return column.rowHeader === true ? (
            <th
              role="rowheader"
              scope="row"
              key={column.name}
              className={className}
              data-label={label}
            >
              {column.cell(entry, index)}
            </th>
          ) : (
            <td role="cell" key={column.name} className={className} data-label={label}>
              {column.cell(entry, index)}
            </td>
          );
        })}
      </tr>
    );
  };

  const line = (content: ReactNode, key: string, lineTestId?: string) => (
    <tr role="row" key={key}>
      <td role="cell" colSpan={across} className={styles.muted} data-testid={lineTestId}>
        {content}
      </td>
    </tr>
  );

  const count =
    props.rows !== undefined
      ? props.rows.length
      : props.groups.reduce((total, group) => total + group.rows.length, 0);

  return (
    <div ref={setBox} className={styles.tableWrap} role="region" aria-label={label} tabIndex={0}>
      <table
        role="table"
        className={joined(styles.table, stacks && styles.stack)}
        data-testid={testId}
      >
        <thead role="rowgroup">
          <tr role="row">
            {columns.map((column) => (
              <th
                role="columnheader"
                scope="col"
                key={column.name}
                className={column.num === true ? styles.num : undefined}
                aria-sort={
                  column.sort === undefined ? undefined : column.sort.active ? 'other' : 'none'
                }
              >
                {column.sort === undefined ? (
                  column.name
                ) : (
                  <button type="button" className={styles.sortButton} onClick={column.sort.onSort}>
                    {column.name}
                  </button>
                )}
              </th>
            ))}
          </tr>
        </thead>
        <tbody role="rowgroup">
          {props.rows !== undefined
            ? props.rows.map(row)
            : props.groups.flatMap((group) => [
                <tr
                  role="row"
                  key={`group:${group.key}`}
                  className={styles.dayRow}
                  data-testid={group.testId}
                >
                  <th role="rowheader" scope="rowgroup" colSpan={across}>
                    {group.title}
                  </th>
                </tr>,
                ...group.rows.map(row),
              ])}
          {count === 0 && empty !== undefined && line(empty, 'empty')}
          {note !== undefined && line(note.content, 'note', note.testId)}
        </tbody>
      </table>
    </div>
  );
}
