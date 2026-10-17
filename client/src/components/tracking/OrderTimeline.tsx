import type { Schemas } from '../../api/typedClient';
import { OrderTrackingStep, OrderTrackingStepState } from '../../types';
import { formatShortDateTime, formatTime } from '../../utils/dates';
import './OrderTimeline.css';

type Step = Schemas['OrderTrackingStepDto'];

// The same palette as the states of an order (StatusChip): new, confirmed, preparing, ready, done, cancelled.
const STEP_CHIP: Record<OrderTrackingStep, string> = {
  [OrderTrackingStep.Received]: 'ta-chip-new',
  [OrderTrackingStep.Confirmed]: 'ta-chip-confirmed',
  [OrderTrackingStep.Preparing]: 'ta-chip-preparing',
  [OrderTrackingStep.Ready]: 'ta-chip-ready',
  [OrderTrackingStep.OnTheWay]: 'ta-chip-ready',
  [OrderTrackingStep.Delivered]: 'ta-chip-done',
  [OrderTrackingStep.Cancelled]: 'ta-chip-cancelled',
};

/** Where the order is, in the words the server gives it («En preparación», «En camino»). */
export function OrderStepChip({ step, label }: { step: OrderTrackingStep; label: string }) {
  return <span className={`ta-chip ${STEP_CHIP[step] ?? 'ta-chip-done'}`}>{label}</span>;
}

const STATE_TEXT: Record<OrderTrackingStepState, string> = {
  [OrderTrackingStepState.Done]: 'hecho',
  [OrderTrackingStepState.Current]: 'ahora',
  [OrderTrackingStepState.Upcoming]: 'pendiente',
};

function markIcon(step: Step): string {
  if (step.step === OrderTrackingStep.Cancelled) return 'ri-close-line';
  if (step.state === OrderTrackingStepState.Done) return 'ri-check-line';
  if (step.state === OrderTrackingStepState.Current) return 'ri-time-line';
  return 'ri-checkbox-blank-circle-line';
}

function stateClass(step: Step): string {
  if (step.step === OrderTrackingStep.Cancelled) return 'ot-cancelled';
  if (step.state === OrderTrackingStepState.Done) return 'ot-done';
  return step.state === OrderTrackingStepState.Current ? 'ot-current' : 'ot-upcoming';
}

/**
 * The steps of an order as the server gives them (Fase 24C): each with the time it reached it, a step the business skipped
 * done without a time. A time of another day than the order's first step carries its date.
 */
export default function OrderTimeline({ steps }: { steps: Step[] }) {
  const firstDay = steps[0]?.at ? new Date(steps[0].at).toDateString() : null;
  const when = (at: string) => (new Date(at).toDateString() === firstDay ? formatTime(at) : formatShortDateTime(at));

  return (
    <ol className="ot-timeline" aria-label="Seguimiento del pedido">
      {steps.map((step) => (
        <li
          key={step.step}
          className={`ot-step ${stateClass(step)}`}
          aria-current={step.state === OrderTrackingStepState.Current ? 'step' : undefined}
        >
          <span className="ot-mark" aria-hidden="true"><i className={markIcon(step)}></i></span>
          <span className="ot-text">
            <span className="ot-label">{step.label}</span>
            <span className="visually-hidden">, {STATE_TEXT[step.state]}</span>
            {step.at && <time className="ot-time" dateTime={step.at}>{when(step.at)}</time>}
          </span>
        </li>
      ))}
    </ol>
  );
}
