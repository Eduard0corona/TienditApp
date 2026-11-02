import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { publicMenuApi } from '../../api/publicMenuApi';
import { ProblemPhotosSender } from './ProblemPhotos';

vi.mock('../../api/publicMenuApi', () => ({ publicMenuApi: { addAppointmentPhoto: vi.fn() } }));

const api = vi.mocked(publicMenuApi);
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';
const offline = { isAxiosError: true, request: {}, message: 'Network Error' };
const full = { isAxiosError: true, response: { status: 400, data: { detail: 'Tu visita ya tiene sus 5 fotos.' } } };

function picked(name: string) {
  return { id: name, file: new File([name], `${name}.jpg`, { type: 'image/jpeg' }), preview: `blob:${name}` };
}

describe('ProblemPhotosSender (Fase 29H)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.spyOn(URL, 'createObjectURL').mockImplementation((file) => `blob:${(file as File).name}`);
  });

  it('sends one at a time, says which failed and why, and sends again only what failed for the connection', async () => {
    const user = userEvent.setup();
    const onSent = vi.fn();
    api.addAppointmentPhoto
      .mockResolvedValueOnce({ problemPhotos: 1, canAddPhotos: true } as never)
      .mockRejectedValueOnce(offline)
      .mockResolvedValueOnce({ problemPhotos: 2, canAddPhotos: true } as never);
    render(<ProblemPhotosSender token={LINK} sent={0} canAdd initial={[picked('fuga'), picked('pared')]} onSent={onSent} />);

    expect(await screen.findByText(/Una foto no se envió: No hay conexión/)).toBeInTheDocument();
    expect(screen.getByText('Enviada')).toBeInTheDocument();
    expect(screen.getByText('No se envió')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar' }));

    expect(await screen.findByText('Enviaste 2 fotos de lo que pasa.')).toBeInTheDocument();
    expect(api.addAppointmentPhoto.mock.calls.map(([token, file]) => [token, file.name])).toEqual(
      [[LINK, 'fuga.jpg'], [LINK, 'pared.jpg'], [LINK, 'pared.jpg']]);
    expect(onSent).toHaveBeenLastCalledWith({ problemPhotos: 2, canAddPhotos: true });
    expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
  });

  it('does not offer to send again what the visit refused, and offers more only while it takes them', async () => {
    const user = userEvent.setup();
    api.addAppointmentPhoto
      .mockResolvedValueOnce({ problemPhotos: 4, canAddPhotos: true } as never)
      .mockRejectedValueOnce(full);
    render(<ProblemPhotosSender token={LINK} sent={3} canAdd onSent={vi.fn()} />);

    // Two fit: three were sent before.
    await user.upload(screen.getByTestId('problem-photos-input'), [new File(['a'], 'a.jpg', { type: 'image/jpeg' })]);
    expect(await screen.findByText('Enviaste una foto de lo que pasa.')).toBeInTheDocument();
    await user.upload(screen.getByTestId('problem-photos-input'), [
      new File(['b'], 'b.jpg', { type: 'image/jpeg' }), new File(['c'], 'c.jpg', { type: 'image/jpeg' }),
    ]);
    expect(screen.getByRole('alert')).toHaveTextContent('Caben 5 fotos: una se quedó fuera.');

    expect(await screen.findByText('Una foto no se envió: Tu visita ya tiene sus 5 fotos.')).toBeInTheDocument();
    expect(api.addAppointmentPhoto).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('button', { name: 'Reintentar' })).not.toBeInTheDocument();
  });

  it('offers no more once the visit has its five', async () => {
    const user = userEvent.setup();
    api.addAppointmentPhoto.mockResolvedValueOnce({ problemPhotos: 5, canAddPhotos: false } as never);
    render(<ProblemPhotosSender token={LINK} sent={4} canAdd onSent={vi.fn()} />);

    await user.upload(screen.getByTestId('problem-photos-input'), [new File(['a'], 'a.jpg', { type: 'image/jpeg' })]);

    expect(await screen.findByText('Enviaste una foto de lo que pasa.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument();
  });
});
