import { afterEach, describe, expect, it, vi } from 'vitest';
import { MAX_PHOTO_BYTES, forgetPreview, photoCount, pickPhotos, previewOf } from './problemPhotos';

function photo(name: string, type = 'image/jpeg', size = 1024): File {
  const file = new File(['x'], name, { type });
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

describe('problem photos (Fase 29H)', () => {
  afterEach(() => vi.restoreAllMocks());

  it('takes JPG, PNG and WebP photos up to 10 MB, as many as fit, and says why the others stay out', () => {
    vi.spyOn(URL, 'createObjectURL').mockImplementation((file) => `blob:${(file as File).name}`);

    const { picked, refused } = pickPhotos([
      photo('fuga.jpg'),
      photo('calentador.heic', 'image/heic'),
      photo('pared.png', 'image/png'),
      photo('grande.jpg', 'image/jpeg', MAX_PHOTO_BYTES + 1),
      photo('techo.webp', 'image/webp', MAX_PHOTO_BYTES),
      photo('otra.jpg'),
    ], 3);

    expect(picked.map((item) => item.file.name)).toEqual(['fuga.jpg', 'pared.png', 'techo.webp']);
    expect(new Set(picked.map((item) => item.id)).size).toBe(3);
    expect(refused).toBe('calentador.heic no es una foto JPG, PNG o WebP. grande.jpg pesa más de 10 MB. Caben 5 fotos: una se quedó fuera.');
  });

  it('takes none when no room is left, and says it', () => {
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:x');

    expect(pickPhotos([photo('a.jpg'), photo('b.jpg')], 0)).toEqual({ picked: [], refused: 'Caben 5 fotos: 2 se quedaron fuera.' });
    expect(pickPhotos([photo('a.jpg')], 1).refused).toBeNull();
  });

  it('takes a photo whose type the phone left empty, by its name', () => {
    const { picked, refused } = pickPhotos([photo('IMG_2041.JPG', ''), photo('notas.txt', '')], 5);

    expect(picked.map((item) => item.file.name)).toEqual(['IMG_2041.JPG']);
    expect(refused).toBe('notas.txt no es una foto JPG, PNG o WebP.');
  });

  it('makes one preview per photo, and lets go of it when the photo is taken out', () => {
    const create = vi.spyOn(URL, 'createObjectURL').mockImplementation((file) => `blob:${(file as File).name}`);
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const fuga = photo('fuga.jpg');

    expect([previewOf(fuga), previewOf(fuga)]).toEqual(['blob:fuga.jpg', 'blob:fuga.jpg']);
    expect(create).toHaveBeenCalledTimes(1);
    forgetPreview(fuga);
    forgetPreview(fuga);
    expect(revoke).toHaveBeenCalledTimes(1);
    expect(revoke).toHaveBeenCalledWith('blob:fuga.jpg');
  });

  it('counts the photos in words', () => {
    expect(photoCount(1)).toBe('una foto');
    expect(photoCount(3)).toBe('3 fotos');
  });
});
