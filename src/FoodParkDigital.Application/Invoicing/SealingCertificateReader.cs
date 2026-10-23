using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FoodParkDigital.Application.Common.Models;
using FoodParkDigital.Domain.Fiscal;
using FoodParkDigital.Domain.Fiscal.Entities;

namespace FoodParkDigital.Application.Invoicing;

/// <summary>What identifies a CSD the business uploaded, once checked.</summary>
public sealed record SealingCertificate(string Number, string Rfc, DateTimeOffset ValidFrom, DateTimeOffset ValidTo);

/// <summary>
/// Reads the CSD a business uploads (Fase 28A, ADR 0045) before it reaches the PAC: the .cer is an X.509 certificate of
/// the SAT for sealing invoices (not the e.firma, which also encrypts), it carries an RFC, and the .key opens with the
/// password and belongs to that certificate. The number of a SAT certificate is its serial number read as digits.
/// Nothing here is kept or logged: the key and the password only go on to the PAC.
/// </summary>
public static class SealingCertificateReader
{
    public const int MaxFileBytes = 16 * 1024;
    public const int MaxPasswordLength = 100;

    public const string NotACertificateMessage = "El archivo .cer no es un certificado del SAT.";
    public const string ElectronicSignatureMessage =
        "Este certificado es de tu e.firma (FIEL). Sube el certificado de sello digital (CSD), el que el SAT da para facturar.";
    public const string NoRfcMessage = "El certificado no trae un RFC del SAT.";
    public const string WrongPasswordMessage =
        "La contraseña de la llave privada no es correcta o el archivo .key no es una llave privada.";
    public const string KeyMismatchMessage = "La llave privada (.key) no es la de este certificado (.cer).";

    // x500UniqueIdentifier: the SAT writes «RFC / CURP» (or «RFC / RFC of the legal representative») there.
    private const string UniqueIdentifierOid = "2.5.4.45";

    public static Result<SealingCertificate> Read(byte[] certificateFile, byte[] keyFile, string password)
    {
        if (certificateFile.Length is 0 or > MaxFileBytes)
            return Result.Failure<SealingCertificate>(NotACertificateMessage);
        if (keyFile.Length is 0 or > MaxFileBytes || string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
            return Result.Failure<SealingCertificate>(WrongPasswordMessage);

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadCertificate(certificateFile);
        }
        catch (CryptographicException)
        {
            return Result.Failure<SealingCertificate>(NotACertificateMessage);
        }

        using (certificate)
        {
            // The e.firma can also encrypt and agree keys; a CSD only signs.
            var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault()?.KeyUsages;
            if (usage is null || !usage.Value.HasFlag(X509KeyUsageFlags.DigitalSignature))
                return Result.Failure<SealingCertificate>(NotACertificateMessage);
            if (usage.Value.HasFlag(X509KeyUsageFlags.DataEncipherment) || usage.Value.HasFlag(X509KeyUsageFlags.KeyAgreement))
                return Result.Failure<SealingCertificate>(ElectronicSignatureMessage);

            var rfc = RfcOf(certificate);
            if (rfc is null)
                return Result.Failure<SealingCertificate>(NoRfcMessage);
            var number = NumberOf(certificate);
            if (number is null)
                return Result.Failure<SealingCertificate>(NotACertificateMessage);

            using var publicKey = certificate.GetRSAPublicKey();
            if (publicKey is null)
                return Result.Failure<SealingCertificate>(NotACertificateMessage);
            using var privateKey = RSA.Create();
            try
            {
                privateKey.ImportEncryptedPkcs8PrivateKey(Encoding.UTF8.GetBytes(password), keyFile, out _);
            }
            catch (CryptographicException)
            {
                return Result.Failure<SealingCertificate>(WrongPasswordMessage);
            }

            var expected = publicKey.ExportParameters(false);
            var actual = privateKey.ExportParameters(false);
            if (!expected.Modulus.AsSpan().SequenceEqual(actual.Modulus) || !expected.Exponent.AsSpan().SequenceEqual(actual.Exponent))
                return Result.Failure<SealingCertificate>(KeyMismatchMessage);

            return Result.Success(new SealingCertificate(
                number, rfc, new DateTimeOffset(certificate.NotBefore.ToUniversalTime()), new DateTimeOffset(certificate.NotAfter.ToUniversalTime())));
        }
    }

    /// <summary>The first RFC of the subject's unique identifier.</summary>
    private static string? RfcOf(X509Certificate2 certificate)
    {
        foreach (var name in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            if (name.GetSingleElementType().Value != UniqueIdentifierOid)
                continue;
            var value = name.GetSingleElementValue();
            var rfc = value?.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant();
            if (SatCatalog.IsValidRfc(rfc))
                return rfc;
        }
        return null;
    }

    /// <summary>A SAT certificate's serial number holds the ASCII digits of its 20-digit number.</summary>
    private static string? NumberOf(X509Certificate2 certificate)
    {
        var serial = certificate.SerialNumberBytes.Span;
        if (serial.Length != InvoicingProfile.CertificateNumberLength)
            return null;
        // SerialNumberBytes is big-endian, as the certificate writes it.
        Span<char> digits = stackalloc char[InvoicingProfile.CertificateNumberLength];
        for (var i = 0; i < serial.Length; i++)
        {
            if (serial[i] is < (byte)'0' or > (byte)'9')
                return null;
            digits[i] = (char)serial[i];
        }
        return new string(digits);
    }
}
