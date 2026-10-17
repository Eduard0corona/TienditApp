import { describe, expect, it } from 'vitest';
import { toPrivateFilePath } from './privateFiles';

const tenant = '0b6f1c7e-2d1a-4c3b-9e8f-1a2b3c4d5e6f';
const file = '9f8e7d6c-5b4a-4321-8765-0fedcba98765';
const key = `orders/${tenant}/payment-evidence/202610/${file}.webp`;

describe('private files', () => {
  it('opens receipts and service photos through the API, also the ones uploaded before they became private', () => {
    expect(toPrivateFilePath(`/files/${key}`)).toBe(`/files/${key}`);
    expect(toPrivateFilePath(`/uploads/${key}`)).toBe(`/files/${key}`);
    expect(toPrivateFilePath(`https://cuenta.blob.core.windows.net/uploads/${key}`)).toBe(`/files/${key}`);
  });

  it('never points the screen at another address', () => {
    expect(toPrivateFilePath('https://tracker.example/pixel.png')).toBeNull();
    expect(toPrivateFilePath(`http://cuenta.blob.core.windows.net/uploads/${key}`)).toBeNull();
    expect(toPrivateFilePath(`//evil.example/${key}`)).toBeNull();
    expect(toPrivateFilePath(`/uploads/../${key}`)).toBeNull();
    expect(toPrivateFilePath(`/files/orders/${tenant}/products/202610/${file}.webp`)).toBeNull();
    expect(toPrivateFilePath(null)).toBeNull();
  });
});
