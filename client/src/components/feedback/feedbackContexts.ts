import { createContext } from 'react';

export type ToastVariant = 'success' | 'error' | 'info' | 'warning';

export interface ToastOptions {
  /** Short line shown in bold above the message. */
  title?: string;
  /** Milliseconds before it closes by itself; errors stay longer by default. */
  durationMs?: number;
}

export interface ToastApi {
  show: (variant: ToastVariant, message: string, options?: ToastOptions) => void;
  success: (message: string, options?: ToastOptions) => void;
  error: (message: string, options?: ToastOptions) => void;
  info: (message: string, options?: ToastOptions) => void;
  warning: (message: string, options?: ToastOptions) => void;
}

export interface ConfirmOptions {
  title: string;
  /** What is affected, by name: "Se cancelará el pedido ORD-000123." */
  message?: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Red confirm button for destructive actions. */
  danger?: boolean;
}

export interface PromptOptions extends ConfirmOptions {
  inputLabel: string;
  placeholder?: string;
  defaultValue?: string;
  /** The confirm button stays disabled until something is written. */
  required?: boolean;
  maxLength?: number;
  multiline?: boolean;
}

export interface ConfirmApi {
  /** Resolves true when confirmed, false when cancelled or closed. */
  confirm: (options: ConfirmOptions) => Promise<boolean>;
  /** Resolves with the trimmed text when confirmed, or null when cancelled. */
  prompt: (options: PromptOptions) => Promise<string | null>;
}

export const ToastContext = createContext<ToastApi | null>(null);
export const ConfirmContext = createContext<ConfirmApi | null>(null);
