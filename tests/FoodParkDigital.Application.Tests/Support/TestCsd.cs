using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FoodParkDigital.Application.Tests.Support;

/// <summary>
/// CSD made the way the SAT makes them (the number in the serial, the RFC in the unique identifier, the key usage of a CSD
/// or of an e.firma), so no key is kept in the repository (Fase 28A).
/// </summary>
internal static class TestCsd
{
    public const string Password = "12345678a";
    public const string Number = "30001000000400002434";
    public static readonly DateTimeOffset From = new(2023, 5, 18, 11, 43, 51, TimeSpan.Zero);
    public static readonly DateTimeOffset To = new(2027, 5, 18, 11, 43, 51, TimeSpan.Zero);

    /// <summary>A certificate as the SAT issues it, and its key encrypted with the password as a .key file.</summary>
    public static (byte[] Cer, byte[] Key) Make(
        string? uniqueIdentifier = "EKU9003173C9 / VADA800927HSRSRL05",
        X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
        byte[]? serial = null,
        string number = Number,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        using var rsa = RSA.Create(2048);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName("ESCUELA KEMPER URGATE SA DE CV");
        subject.AddOrganizationName("ESCUELA KEMPER URGATE SA DE CV");
        if (uniqueIdentifier is not null)
            subject.Add("2.5.4.45", uniqueIdentifier, UniversalTagNumber.PrintableString);
        var name = subject.Build();

        var request = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        using var certificate = request.Create(
            name, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1), from ?? From, to ?? To,
            serial ?? Encoding.ASCII.GetBytes(number));

        var key = rsa.ExportEncryptedPkcs8PrivateKey(
            Encoding.UTF8.GetBytes(Password), new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048));
        return (certificate.Export(X509ContentType.Cert), key);
    }
}
