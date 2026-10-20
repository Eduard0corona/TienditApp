import type { OrderPaymentMethod } from '../../types';
import { OrderPaymentMethod as PaymentMethod } from '../../types';

/** What a customer pays in person, with its icon: every one enters a drawer. */
export const IN_PERSON_METHODS: Array<{ value: OrderPaymentMethod; icon: string }> = [
  { value: PaymentMethod.Cash, icon: 'ri-money-dollar-circle-line' },
  { value: PaymentMethod.Card, icon: 'ri-bank-card-line' },
  { value: PaymentMethod.Transfer, icon: 'ri-exchange-dollar-line' },
  { value: PaymentMethod.DigitalWallet, icon: 'ri-smartphone-line' },
  { value: PaymentMethod.Other, icon: 'ri-more-line' },
];
