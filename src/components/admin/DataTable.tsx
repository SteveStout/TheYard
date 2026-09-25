/**
 * Every table on the Admin tab, drawn one way (1.0.3.30; ADR: The tweaks pass, the
 * addendum on tables on a phone). A card names its columns once,
 * and the name is both the column's header and, on a phone, the label beside
 * each value.
 *
 * Under 640 px a table of three or more columns stacks: each row becomes a
 * block led by its first column, with every other value on a line of its own
 * behind its column's name. The pictures of 1.0.3.29 at 390 showed why: the log
 * gave its message twenty pixels and rows three hundred tall, and the store,
 * the machines and the backends were cut at the phone's edge. A table of two
 * columns is already a label and a value, and stays a table.
 *
 * A browser that lays a table's parts out as blocks may stop telling a screen
 * reader it is a table (Safari has), so every part carries its role, and the
 * reader hears the same table at every width.
 */
import type { ReactNode } from 'react';
import styles from '../AdminPanel.module.css';

export type Column<Row> = {
  /** The header's words, and the label beside each value when a phone stacks the row. */
  name: string;
  /** What the cell holds; the index is the row's place among the rows it is drawn with. */
  cell: (row: Row, index: number) => ReactNode;
  /** The small mono type of an identifier, a time or a reading, which may break at any character. */
  mono?: boolean;
  /** A number: tabular figures, right-aligned in a table and never broken. */
  num?: boolean;
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

/** A table of three or more columns stacks on a phone; two are a label and a value already. */
export const STACK_FROM_COLUMNS = 3;

const joined = (...names: (string | false | undefined)[]) =>
  names.filter((name) => typeof name === 'string' && name !== '').join(' ') || undefined;

export function DataTable<Row>(props: DataTableProps<Row>) {
  const { label, columns, rowKey, testId, rowProps, empty, note } = props;
  const stacks = columns.length >= STACK_FROM_COLUMNS;
  const across = columns.length;

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
    <div className={styles.tableWrap} role="region" aria-label={label} tabIndex={0}>
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
