import { describe, expect, it } from 'vitest';
import { coordinatesError, formatCoordinates, formatDistance, mapPointUrl, parseCoordinates } from './geo';

describe('geo', () => {
  it('reads the coordinates Google Maps copies, or a link of it with the point inside', () => {
    const zocalo = { latitude: 19.043577, longitude: -98.198626 };
    expect(parseCoordinates('19.0435774, -98.1986262')).toEqual(zocalo);
    expect(parseCoordinates(' 19.0435774 -98.1986262 ')).toEqual(zocalo);
    expect(parseCoordinates('19.043577,-98.198626')).toEqual(zocalo);
    // A place: its point (!3d!4d), not the center of the view (@).
    expect(parseCoordinates('https://www.google.com/maps/place/Z%C3%B3calo/@19.05,-98.2,17z/data=!3d19.0435774!4d-98.1986262')).toEqual(zocalo);
    expect(parseCoordinates('https://maps.google.com/?q=19.0435774,-98.1986262')).toEqual(zocalo);
    // Shared from a phone, with its comma written as %2C.
    expect(parseCoordinates('https://www.google.com/maps/search/?api=1&query=19.0435774%2C-98.1986262')).toEqual(zocalo);
  });

  it('never takes where the map of a link was looking for the place', () => {
    // A map, a route or a list of results: its @ is the center of the view, wherever the owner had scrolled.
    expect(parseCoordinates('https://www.google.com/maps/@19.0435774,-98.1986262,17z')).toBe('invalid');
    expect(parseCoordinates('https://www.google.com/maps/dir/Puebla/Cholula/@19.05,-98.25,13z')).toBe('invalid');
    // A malformed escape is read as it was written.
    expect(parseCoordinates('19.0435774, -98.1986262%')).toBe('invalid');
  });

  it('says nothing for an empty field, and refuses what is not a point on the map', () => {
    expect(parseCoordinates('  ')).toBeNull();
    expect(parseCoordinates('por el parque')).toBe('invalid');
    expect(parseCoordinates('https://maps.app.goo.gl/AbCd123')).toBe('invalid');
    expect(parseCoordinates('91, -98.19')).toBe('invalid');
    expect(parseCoordinates('19.04, -181')).toBe('invalid');
    // What a device without a position writes.
    expect(parseCoordinates('0, 0')).toBe('invalid');
  });

  it('says why a text is not a point: a link without one, or what it could not read', () => {
    expect(coordinatesError('https://maps.app.goo.gl/AbCd123')).toMatch(/^Ese enlace no trae el punto exacto/);
    expect(coordinatesError('https://www.google.com/maps/@19.04,-98.19,17z')).toMatch(/^Ese enlace no trae el punto exacto/);
    expect(coordinatesError('por el parque')).toMatch(/^No entendemos esas coordenadas/);
  });

  it('writes a point as Google Maps copies it, and opens it in the map', () => {
    expect(formatCoordinates({ latitude: 19.0435774, longitude: -98.1986262 })).toBe('19.043577, -98.198626');
    expect(mapPointUrl({ latitude: 19.043577, longitude: -98.198626 }))
      .toBe('https://www.google.com/maps/search/?api=1&query=19.043577,-98.198626');
  });

  it('says how far a business is in meters close by and in kilometers further', () => {
    expect(formatDistance(0)).toBe('a menos de 100 m');
    expect(formatDistance(0.3)).toBe('a 300 m');
    expect(formatDistance(0.9)).toBe('a 900 m');
    expect(formatDistance(0.95)).toBe('a 1 km');
    expect(formatDistance(1)).toBe('a 1 km');
    expect(formatDistance(2.45)).toBe('a 2.5 km');
    expect(formatDistance(12.4)).toBe('a 12 km');
    expect(formatDistance(1234)).toBe('a 1,234 km');
  });
});
