import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it } from 'vitest';
import ThemeToggle from './ThemeToggle';

const icon = () => screen.getByRole('button', { name: 'Cambiar entre modo claro y oscuro' }).querySelector('i');

afterEach(() => {
  document.documentElement.setAttribute('data-bs-theme', 'light');
  window.localStorage.clear();
});

describe('ThemeToggle', () => {
  it('switches the page between light and dark, remembers it and shows the mode it is in', async () => {
    document.documentElement.setAttribute('data-bs-theme', 'light');
    render(<ThemeToggle />);
    expect(icon()).toHaveClass('ri-moon-line');

    await userEvent.click(screen.getByRole('button', { name: 'Cambiar entre modo claro y oscuro' }));
    expect(document.documentElement).toHaveAttribute('data-bs-theme', 'dark');
    expect(window.localStorage.getItem('tienditapp.theme')).toBe('dark');
    expect(icon()).toHaveClass('ri-sun-line');
    expect(icon()).not.toHaveClass('ri-moon-line');

    await userEvent.click(screen.getByRole('button', { name: 'Cambiar entre modo claro y oscuro' }));
    expect(document.documentElement).toHaveAttribute('data-bs-theme', 'light');
    expect(window.localStorage.getItem('tienditapp.theme')).toBe('light');
    expect(icon()).toHaveClass('ri-moon-line');
  });

  it('starts from the mode the page already has (a dark theme restored on load)', () => {
    document.documentElement.setAttribute('data-bs-theme', 'dark');
    render(<ThemeToggle />);
    expect(icon()).toHaveClass('ri-sun-line');
  });
});
