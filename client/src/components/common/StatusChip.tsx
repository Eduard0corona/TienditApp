import type { OrderStatus } from '../../types';
import { getOrderStatusLabel } from '../../utils/labels';

// One palette for order states across the app: new (amber), confirmed (blue), preparing (violet),
// ready (green), delivered or completed (neutral), cancelled (red). Always with text.
const CHIP_CLASS: Record<number, string> = {
  0: 'ta-chip-new',
  1: 'ta-chip-confirmed',
  2: 'ta-chip-preparing',
  3: 'ta-chip-ready',
  4: 'ta-chip-confirmed',
  5: 'ta-chip-preparing',
  6: 'ta-chip-done',
  7: 'ta-chip-done',
  99: 'ta-chip-cancelled',
};

export default function StatusChip({ status }: { status: OrderStatus }) {
  return <span className={`ta-chip ${CHIP_CLASS[status] ?? 'ta-chip-done'}`}>{getOrderStatusLabel(status)}</span>;
}
