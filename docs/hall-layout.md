# Auditorium layout data

`hall-layout.json` is the machine-readable seat catalogue transcribed from
`hall.xlsx`, sheet `Лист3`. It contains one record per seat, including the
source worksheet cell and coordinates, and no event state. The catalogue
contains regular rows plus the optional row `12А`; enabling that row is an
event-specific setting, not a change to the shared physical catalogue.

## Format

- `seatId` is the normalized row label followed by `-` and the seat number.
- `rowLabel` is the row identifier without the workbook's display suffix
  `ряд`; `sourceRowLabel` preserves the original cell text, including the
  workbook's `12Аряд` label.
- `seatNumber` is the integer printed in the workbook.
- Coordinates use the 1-based Excel worksheet grid: `x` is the column number,
  `y` is the worksheet row number. They are not physical measurements.
- `sourceCell` identifies the cell from which the seat number was read.

The workbook enumerates 643 distinct seats across the 27 regular rows
(`1`–`11`, `12`, `13`–`27`) and the optional `12А` row. The extra row can be
enabled per event at creation or while reservations are open. Its display
label is exactly `12А`; the row is not renumbered as `28`. The catalogue
includes its seats regardless of whether any particular event enables it.

The catalogue intentionally contains no reservation, availability, VIP, or
organization fields. Those values are event-specific and must be stored and
returned separately for each event. Customers see only row/seat labels and
whether each seat is available; administrators can see designations and
occupancy.
