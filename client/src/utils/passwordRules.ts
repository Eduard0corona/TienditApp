/**
 * The password rules of the API (PasswordPolicy): at least 8 characters with an uppercase, a lowercase and a number.
 * The API also refuses common passwords and those with the person's email or name, and says so.
 */
export const PASSWORD_MIN_LENGTH = 8;

export const PASSWORD_HINT = `Usa al menos ${PASSWORD_MIN_LENGTH} caracteres, con mayúscula, minúscula y número.`;

/** react-hook-form rule for a password being chosen. */
export const newPasswordPattern = {
  value: /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$/,
  message: PASSWORD_HINT,
};
