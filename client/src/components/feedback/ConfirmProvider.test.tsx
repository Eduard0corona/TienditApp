import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { useConfirm } from '../../hooks/useConfirm';
import ConfirmProvider from './ConfirmProvider';

function Asker({ onAnswer }: { onAnswer: (answer: boolean | string | null) => void }) {
  const { confirm, prompt } = useConfirm();
  return (
    <>
      <button type="button" onClick={() => void confirm({ title: 'Cancelar el pedido', message: 'Se cancelará el pedido ORD-000123.' }).then(onAnswer)}>
        Cancelar pedido
      </button>
      <button type="button" onClick={() => void prompt({ title: 'Motivo del ajuste', inputLabel: 'Motivo', required: true }).then(onAnswer)}>
        Ajustar
      </button>
    </>
  );
}

describe('ConfirmProvider', () => {
  it('names the confirmation after its title, so a screen reader says what is being confirmed', async () => {
    const onAnswer = vi.fn();
    render(<ConfirmProvider><Asker onAnswer={onAnswer} /></ConfirmProvider>);

    await userEvent.click(screen.getByRole('button', { name: 'Cancelar pedido' }));
    const dialog = screen.getByRole('dialog', { name: 'Cancelar el pedido' });
    expect(dialog).toHaveTextContent('Se cancelará el pedido ORD-000123.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar' }));

    expect(onAnswer).toHaveBeenCalledWith(true);
  });

  it('names the prompt after its title too', async () => {
    const onAnswer = vi.fn();
    render(<ConfirmProvider><Asker onAnswer={onAnswer} /></ConfirmProvider>);

    await userEvent.click(screen.getByRole('button', { name: 'Ajustar' }));
    const dialog = screen.getByRole('dialog', { name: 'Motivo del ajuste' });
    await userEvent.type(within(dialog).getByLabelText('Motivo'), ' Merma ');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Confirmar' }));

    expect(onAnswer).toHaveBeenCalledWith('Merma');
  });
});
