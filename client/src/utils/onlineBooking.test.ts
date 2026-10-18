import { describe, expect, it } from 'vitest';
import { AppointmentStatus } from '../types';
import { bookingPath, customerAppointmentChip, describeDuration, shortDayLabel, upperFirst } from './onlineBooking';

describe('onlineBooking', () => {
  it('says how long a service takes', () => {
    expect(describeDuration(45)).toBe('45 min');
    expect(describeDuration(60)).toBe('1 h');
    expect(describeDuration(90)).toBe('1 h 30 min');
  });

  it('names a day of the business calendar whatever the device time zone', () => {
    expect(shortDayLabel('2026-10-05')).toBe('Lun 5');
    expect(shortDayLabel('2026-10-04')).toBe('Dom 4');
  });

  it('uses the colors of the states, never the accent', () => {
    expect(customerAppointmentChip(AppointmentStatus.Requested)).toBe('ta-chip-new');
    expect(customerAppointmentChip(AppointmentStatus.Scheduled)).toBe('ta-chip-confirmed');
    expect(customerAppointmentChip(AppointmentStatus.Cancelled)).toBe('ta-chip-cancelled');
  });

  it('starts a line with a capital and leads to the booking page', () => {
    expect(upperFirst('lunes 5 de octubre')).toBe('Lunes 5 de octubre');
    expect(bookingPath('barberia-luis')).toBe('/m/barberia-luis/agendar');
  });
});
