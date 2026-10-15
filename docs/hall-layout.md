# Auditorium layout data

`hall-layout.json` is the machine-readable seat catalogue transcribed from
`hall.xlsx`, sheet `Лист3`. It contains one record per seat, including the
source worksheet cell and coordinates, and no event state.

## Format

- `seatId` is the normalized row label followed by `-` and the seat number.
- `rowLabel` is the row identifier without the workbook's display suffix
  `ряд`; `sourceRowLabel` preserves the original cell text, including the
  workbook's `12Аряд` label.
- `seatNumber` is the integer printed in the workbook.
- Coordinates use the 1-based Excel worksheet grid: `x` is the column number,
  `y` is the worksheet row number. They are not physical measurements.
- `sourceCell` identifies the cell from which the seat number was read.

The workbook enumerates 643 distinct seats. Its row identifiers are `1`–`11`,
`12А`, `12`, and `13`–`27` (28 labelled rows). The `12А` and `12` identifiers
are both preserved; no interpretation of the extra label is implied. See
`product-spec.md` for the unresolved row-count discrepancy.

The catalogue intentionally contains no reservation, availability, VIP, or
organization fields. Those values are event-specific and must be stored and
returned separately for each event.
