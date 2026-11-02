import { StrictMode } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { publicMenuApi } from '../../api/publicMenuApi';
import { resetLinkUploads } from '../../hooks/useProblemPhotoUploads';
import { ProblemPhotosSender } from './ProblemPhotos';

vi.mock('../../api/publicMenuApi', () => ({ publicMenuApi: { addAppointmentPhoto: vi.fn(), trackAppointment: vi.fn() } }));

const api = vi.mocked(publicMenuApi);
const LINK = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789-_AbCdE';
const offline = { isAxiosError: true, request: {}, message: 'Network Error' };
const failing = { isAxiosError: true, response: { status: 503, data: {} } };
const full = { isAxiosError: true, response: { status: 400, data: { detail: 'Tu visita ya tiene sus 5 fotos.' } } };
const gone = { isAxiosError: true, response: { status: 404, data: { detail: 'No encontramos esa cita. El enlace pudo haber vencido.' } } };

function picked(name: string) {
  return { id: name, file: new File([name], `${name}.jpg`, { type: 'image/jpeg' }) };
}

function visit(problemPhotos: number, canAddPhotos = problemPhotos < 5) {
  return { problemPhotos, canAddPhotos } as never;
}

describe('ProblemPhotosSender (Fase 29H)', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    resetLinkUploads();
    vi.spyOn(URL, 'createObjectURL').mockImplementation((file) => `blob:${(file as File).name}`);
  });

  it('sends one at a time, says which failed and why, and sends again only what failed for the connection or the server', async () => {
    const user = userEvent.setup();
    api.addAppointmentPhoto
      .mockResolvedValueOnce(visit(1))
      .mockRejectedValueOnce(offline)
      .mockRejectedValueOnce(failing)
      .mockResolvedValueOnce(visit(2))
      .mockResolvedValueOnce(visit(3));
    render(<ProblemPhotosSender token={LINK} sent={0} canAdd initial={[picked('fuga'), picked('pared'), picked('techo')]} />);

    expect(await screen.findByText(/^2 fotos no se mandaron: No hay conexión/)).toBeInTheDocument();
    expect(screen.getByText('Enviada')).toBeInTheDocument();
    expect(screen.getAllByText('No se mandó')).toHaveLength(2);
    // Two going again, never counted as room.
    expect(screen.getByText('Caben 2 más.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Reintentar las fotos que no se mandaron' }));

    expect(await screen.findByText('Listo: se mandaron 3 fotos.')).toBeInTheDocument();
    expect(api.addAppointmentPhoto.mock.calls.map(([token, file]) => [token, file.name])).toEqual(
      [[LINK, 'fuga.jpg'], [LINK, 'pared.jpg'], [LINK, 'techo.jpg'], [LINK, 'pared.jpg'], [LINK, 'techo.jpg']]);
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
    expect(screen.getByText(/Listo/)).toHaveFocus();
  });

  it('reads the visit again after a refusal, and offers no more once it takes none: the refused one only goes away', async () => {
    const user = userEvent.setup();
    api.addAppointmentPhoto.mockResolvedValueOnce(visit(4)).mockRejectedValueOnce(full);
    api.trackAppointment.mockResolvedValueOnce(visit(5, false));
    render(<ProblemPhotosSender token={LINK} sent={3} canAdd />);

    await user.upload(screen.getByTestId('problem-photos-input'), [new File(['a'], 'a.jpg', { type: 'image/jpeg' })]);
    expect(await screen.findByText('Listo: se mandó una foto.')).toBeInTheDocument();
    await user.upload(screen.getByTestId('problem-photos-input'), [
      new File(['b'], 'b.jpg', { type: 'image/jpeg' }), new File(['c'], 'c.jpg', { type: 'image/jpeg' }),
    ]);
    expect(screen.getByRole('alert')).toHaveTextContent('Caben 5 fotos: una se quedó fuera.');

    expect(await screen.findByText('Una foto no se mandó: Tu visita ya tiene sus 5 fotos.')).toBeInTheDocument();
    expect(api.trackAppointment).toHaveBeenCalledWith(LINK);
    expect(screen.queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Reintentar/ })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Quitar la foto 2, que no se mandó' }));
    expect(screen.queryByText(/no se mandó/)).not.toBeInTheDocument();
    expect(screen.getByText('Listo: se mandó una foto.')).toHaveFocus();
  });

  it('stops once the link no longer opens', async () => {
    api.addAppointmentPhoto.mockRejectedValueOnce(gone);
    api.trackAppointment.mockRejectedValueOnce(gone);
    render(<ProblemPhotosSender token={LINK} sent={0} canAdd initial={[picked('fuga'), picked('pared')]} />);

    expect(await screen.findByText(/^2 fotos no se mandaron: No encontramos esa cita/)).toBeInTheDocument();
    expect(api.addAppointmentPhoto).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('button', { name: /Agregar|Reintentar/ })).not.toBeInTheDocument();
  });

  it('offers no more when the page says the visit no longer takes them (cancelled, or its time came)', () => {
    render(<ProblemPhotosSender token={LINK} sent={2} canAdd={false} />);

    expect(screen.queryByRole('button', { name: /Agregar/ })).not.toBeInTheDocument();
    expect(screen.queryByText(/Caben/)).not.toBeInTheDocument();
  });

  it('sends each photo of the request once, also when it shows twice or again after the page moved on', async () => {
    api.addAppointmentPhoto.mockResolvedValueOnce(visit(1)).mockResolvedValueOnce(visit(2));
    const initial = [picked('fuga'), picked('pared')];
    const { unmount } = render(<StrictMode><ProblemPhotosSender token={LINK} sent={0} canAdd initial={initial} /></StrictMode>);
    expect(await screen.findByText('Listo: se mandaron 2 fotos.')).toBeInTheDocument();
    unmount();

    render(<ProblemPhotosSender token={LINK} sent={0} canAdd initial={initial} />);

    expect(screen.getByText('Listo: se mandaron 2 fotos.')).toBeInTheDocument();
    expect(api.addAppointmentPhoto).toHaveBeenCalledTimes(2);
  });
});
