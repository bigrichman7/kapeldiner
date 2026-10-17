import hall from '../../../docs/hall-layout.json';
import type { AdminSeat, UserSeat } from '../types';

type Seat = (typeof hall.seats)[number];

export function SeatMap({
  data,
  selected,
  onToggle,
  disabled = false,
  admin = false,
  enableAdditionalRow12A = false,
  selectedUserId,
  onDesignation,
}: {
  data: UserSeat[] | AdminSeat[];
  selected: Set<string>;
  onToggle?: (seatId: string) => void;
  disabled?: boolean;
  admin?: boolean;
  enableAdditionalRow12A?: boolean;
  selectedUserId?: string;
  onDesignation?: (seatId: string, designation: 'vip' | 'organization' | 'none') => void;
}) {
  const stateBySeat = new Map(data.map((seat) => [seat.seatId, seat]));
  const seats = (hall.seats as Seat[]).filter((seat) =>
    seat.rowLabel !== '12А' || enableAdditionalRow12A,
  );
  const rows = new Map<string, Seat[]>();
  for (const seat of seats) {
    const row = rows.get(seat.rowLabel) ?? [];
    row.push(seat);
    rows.set(seat.rowLabel, row);
  }

  return (
    <div className="seat-map-viewport">
      <div className="stage">СЦЕНА</div>
      <div className="seat-map" role="group" aria-label="Схема мест в зале">
        {[...rows].map(([rowLabel, rowSeats]) => (
          <div className="seat-row" key={rowLabel}>
            <span className="row-label">{rowLabel}</span>
            <div className="seat-row-grid">
              {rowSeats.map((catalogueSeat) => {
                const seat = stateBySeat.get(catalogueSeat.seatId);
                const isSelected = selected.has(catalogueSeat.seatId);
                const isBooked = admin ? (seat as AdminSeat | undefined)?.booked ?? false : !(seat as UserSeat | undefined)?.available;
                const isOwnSeat = admin && (seat as AdminSeat | undefined)?.userId === selectedUserId;
                const designation = admin ? (seat as AdminSeat | undefined)?.designation : null;
                const occupant = admin && isBooked && (seat as AdminSeat).userName;
                const unavailable = disabled || (!isSelected && isBooked && !isOwnSeat)
                  || (!admin && !isSelected && !(seat as UserSeat | undefined)?.available);
                const stateLabel = admin
                  ? isBooked ? `, занято${occupant ? `: ${occupant}` : ''}` : ''
                  : (seat as UserSeat | undefined)?.available ? ', свободно' : ', недоступно';
                const className = [
                  'seat',
                  isSelected ? 'seat-selected' : '',
                  isBooked && !isOwnSeat ? 'seat-booked' : '',
                  designation === 'vip' ? 'seat-vip' : '',
                  designation === 'organization' ? 'seat-organization' : '',
                  isBooked && isOwnSeat ? 'seat-mine' : '',
                ].filter(Boolean).join(' ');
                return (
                  <button
                    key={catalogueSeat.seatId}
                    type="button"
                    className={className}
                    style={{ gridColumn: catalogueSeat.coordinates.x }}
                    disabled={unavailable}
                    aria-pressed={isSelected}
                    aria-label={`Ряд ${rowLabel}, место ${catalogueSeat.seatNumber}${designation ? `, ${designation === 'vip' ? 'VIP' : 'организационное'}` : ''}${stateLabel}`}
                    title={admin && isBooked ? `${catalogueSeat.seatId} · ${occupant ?? 'Занято'}` : catalogueSeat.seatId}
                    onClick={() => admin && onDesignation
                      ? onDesignation(catalogueSeat.seatId, (seat as AdminSeat | undefined)?.designation === 'vip' ? 'organization' : 'vip')
                      : onToggle?.(catalogueSeat.seatId)}
                  >
                    {catalogueSeat.seatNumber}
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export function SeatLegend({ admin = false }: { admin?: boolean }) {
  return (
    <div className="seat-legend" aria-label="Обозначения">
      <span><i className="legend-seat legend-free" /> Свободно</span>
      <span><i className="legend-seat legend-selected" /> Выбрано</span>
      <span><i className="legend-seat legend-booked" /> Занято</span>
      {admin && <>
        <span><i className="legend-seat legend-vip" /> VIP</span>
        <span><i className="legend-seat legend-organization" /> Организационное</span>
      </>}
    </div>
  );
}
