import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ordersApi } from '../../api/ordersApi';
import { useToast } from '../../hooks/useToast';
import ConfirmProvider from '../feedback/ConfirmProvider';
import { ServicePhotoStage, type ServiceOrderPhotoDto, type ServiceOrderPhotosDto } from '../../types';
import { describeServicePhoto } from '../../utils/servicePhotos';
import ServicePhotosPanel from './ServicePhotosPanel';

vi.mock('../../api/ordersApi', () => ({
  ordersApi: {
    getServicePhotos: vi.fn(),
    uploadServicePhoto: vi.fn(),
    addServicePhoto: vi.fn(),
    removeServicePhoto: vi.fn(),
  },
}));
vi.mock('../../hooks/useToast', () => ({ useToast: vi.fn() }));
// The thumbnails fetch their files with the session; here they stay as their placeholder.
vi.mock('../../hooks/usePrivateFile', () => ({ usePrivateFile: () => ({ path: null, url: null, failed: false }) }));

const api = vi.mocked(ordersApi);
const toast = { success: vi.fn(), warning: vi.fn(), error: vi.fn(), info: vi.fn() };
const file = (name: string) => `/files/orders/t1/service-intake/202610/${name}.webp`;

function photo(overrides: Partial<ServiceOrderPhotoDto> = {}): ServiceOrderPhotoDto {
  return { stage: ServicePhotoStage.Before, url: file('a'), atIntake: false, canRemove: false, ...overrides };
}

function photos(overrides: Partial<ServiceOrderPhotosDto> = {}): ServiceOrderPhotosDto {
  return { orderId: 'o1', canAdd: true, photos: [], ...overrides };
}

function renderPanel() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <ConfirmProvider>
        <ServicePhotosPanel orderId="o1" />
      </ConfirmProvider>
    </QueryClientProvider>,
  );
}

describe('ServicePhotosPanel', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(useToast).mockReturnValue(toast as unknown as ReturnType<typeof useToast>);
  });

  it('shows the photos before and after, with their note and who took them, and offers to remove only the removable ones', async () => {
    api.getServicePhotos.mockResolvedValue({ data: photos({ photos: [
      photo({ url: file('intake'), fileName: 'frente.jpg', atIntake: true }),
      photo({ id: 'p1', url: file('mine'), note: 'Mancha en la manga', takenBy: 'Ana', takenAt: '2026-10-17T18:00:00Z', canRemove: true }),
      photo({ id: 'p2', stage: ServicePhotoStage.After, url: file('theirs'), takenBy: 'Luis', takenAt: '2026-10-17T20:00:00Z' }),
    ] }) } as never);

    renderPanel();

    const before = await screen.findByRole('region', { name: 'Fotos de antes' });
    expect(within(before).getByText('Al recibir la orden')).toBeInTheDocument();
    expect(within(before).getByText('Mancha en la manga')).toBeInTheDocument();
    expect(within(before).getByRole('button', { name: 'Quitar la foto Mancha en la manga' })).toBeInTheDocument();
    const after = screen.getByRole('region', { name: 'Fotos de después' });
    expect(within(after).getByText(/^Luis · /)).toBeInTheDocument();
    expect(within(after).queryByRole('button', { name: /Quitar/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /Quitar/ })).toHaveLength(1);
  });

  it('says when a stage has no photos', async () => {
    api.getServicePhotos.mockResolvedValue({ data: photos() } as never);

    renderPanel();

    expect(await screen.findAllByText('Sin fotos.')).toHaveLength(2);
  });

  it('uploads the photo and adds it to the order with its stage and note', async () => {
    api.getServicePhotos.mockResolvedValue({ data: photos() } as never);
    api.uploadServicePhoto.mockResolvedValue({ data: { url: file('nueva'), fileName: 'saco.jpg', contentType: 'image/webp' } } as never);
    api.addServicePhoto.mockResolvedValue({ data: photo({ id: 'p9' }) } as never);
    const user = userEvent.setup();
    renderPanel();

    await user.selectOptions(await screen.findByLabelText('Etapa'), String(ServicePhotoStage.Before));
    await user.type(screen.getByLabelText('Nota (opcional)'), '  Botón flojo  ');
    const picked = new File(['jpg'], 'saco.jpg', { type: 'image/jpeg' });
    await user.upload(screen.getByLabelText('Foto'), picked);
    await user.click(screen.getByRole('button', { name: 'Agregar foto' }));

    await waitFor(() => expect(api.addServicePhoto).toHaveBeenCalledWith('o1', {
      stage: ServicePhotoStage.Before,
      fileUrl: file('nueva'),
      fileName: 'saco.jpg',
      note: 'Botón flojo',
    }));
    expect(api.uploadServicePhoto).toHaveBeenCalledWith(picked);
    expect(toast.success).toHaveBeenCalledWith('Foto agregada.');
    // The note is cleared for the next photo, and the photos are asked for again.
    expect(screen.getByLabelText('Nota (opcional)')).toHaveValue('');
    await waitFor(() => expect(api.getServicePhotos).toHaveBeenCalledTimes(2));
  });

  it('asks for a photo before adding, and shows why the server refused one', async () => {
    api.getServicePhotos.mockResolvedValue({ data: photos() } as never);
    api.uploadServicePhoto.mockResolvedValue({ data: { url: file('nueva'), fileName: 'saco.jpg', contentType: 'image/webp' } } as never);
    api.addServicePhoto.mockRejectedValue({ isAxiosError: true, response: { status: 400, data: { detail: 'Una orden lleva hasta 20 fotos del después.' } } });
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole('button', { name: 'Agregar foto' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Elige o toma una foto.');
    expect(api.uploadServicePhoto).not.toHaveBeenCalled();

    await user.upload(screen.getByLabelText('Foto'), new File(['jpg'], 'saco.jpg', { type: 'image/jpeg' }));
    await user.click(screen.getByRole('button', { name: 'Agregar foto' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Una orden lleva hasta 20 fotos del después.');
  });

  it('removes a photo only after confirming', async () => {
    api.getServicePhotos.mockResolvedValue({ data: photos({ photos: [
      photo({ id: 'p1', note: 'Equivocada', takenBy: 'Ana', takenAt: '2026-10-17T18:00:00Z', canRemove: true }),
    ] }) } as never);
    api.removeServicePhoto.mockResolvedValue({} as never);
    const user = userEvent.setup();
    renderPanel();

    await user.click(await screen.findByRole('button', { name: 'Quitar la foto Equivocada' }));
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Volver' }));
    expect(api.removeServicePhoto).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Quitar la foto Equivocada' }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toHaveTextContent('La foto «Equivocada» deja de verse en la orden.');
    await user.click(within(dialog).getByRole('button', { name: 'Quitar foto' }));

    await waitFor(() => expect(api.removeServicePhoto).toHaveBeenCalledWith('o1', 'p1'));
    expect(toast.success).toHaveBeenCalledWith('Foto quitada.');
  });

  it('takes no more photos for a cancelled order', async () => {
    api.getServicePhotos.mockResolvedValue({ data: photos({ canAdd: false, photos: [photo({ id: 'p1', takenBy: 'Ana' })] }) } as never);

    renderPanel();

    expect(await screen.findByText('La orden está cancelada: ya no se le agregan fotos.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Agregar foto' })).not.toBeInTheDocument();
  });

  it('describes who took a photo and when, or that it came with the order', () => {
    expect(describeServicePhoto(photo({ atIntake: true, takenBy: 'Ana' }))).toBe('Al recibir la orden');
    expect(describeServicePhoto(photo({ takenBy: 'Ana' }))).toBe('Ana');
    expect(describeServicePhoto(photo())).toBe('');
  });
});
