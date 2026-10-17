import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SeatMap } from './SeatMap';

describe('SeatMap', () => {
  it('does not reveal administrator occupancy or designations in the user view', () => {
    render(<SeatMap data={[{ seatId: '1-1', available: false }]} selected={new Set()} />);

    expect(screen.getByRole('button', { name: 'Ряд 1, место 1, недоступно' })).toBeDisabled();
    expect(screen.queryByText(/VIP|организационное|занято:/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Ряд 12А/ })).not.toBeInTheDocument();
  });

  it('renders the optional 12А row only when the event enables it', () => {
    render(
      <SeatMap
        data={[{ seatId: '12А-26', available: true }]}
        selected={new Set()}
        enableAdditionalRow12A
      />,
    );

    expect(screen.getByRole('button', { name: 'Ряд 12А, место 26, свободно' })).toBeInTheDocument();
  });
});
