using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FoodParkDigital.Application.Invoicing;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The CSD a business uploads is checked before it reaches the PAC (Fase 28A, ADR 0045). The certificates are made here
/// the way the SAT makes them (the number in the serial, the RFC in the unique identifier, the key usage of a CSD or of an
/// e.firma), so no key is kept in the repository.
/// </summary>
public class SealingCertificateReaderTests
{
    private const string Password = "12345678a";
    private const string Number = "30001000000400002434";
    private static readonly DateTimeOffset From = new(2023, 5, 18, 11, 43, 51, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2027, 5, 18, 11, 43, 51, TimeSpan.Zero);

    [Fact]
    public void Csd_ShouldGiveItsNumberRfcAndValidity()
    {
        var (cer, key) = Make();

        var read = SealingCertificateReader.Read(cer, key, Password);

        Assert.True(read.IsSuccess, read.Error);
        Assert.Equal(new SealingCertificate(Number, "EKU9003173C9", From, To), read.Value);
    }

    [Fact]
    public void Csd_OfACompany_ShouldGiveTheCompanysRfc_NotItsRepresentatives()
    {
        var (cer, key) = Make(uniqueIdentifier: "EKU9003173C9 / XIQB891116QE4");

        Assert.Equal("EKU9003173C9", SealingCertificateReader.Read(cer, key, Password).Value.Rfc);
    }

    [Fact]
    public void ElectronicSignature_ShouldBeRefused_AsNotTheCsd()
    {
        var (cer, key) = Make(usage: X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation
            | X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.KeyAgreement);

        Assert.Equal(SealingCertificateReader.ElectronicSignatureMessage, SealingCertificateReader.Read(cer, key, Password).Error);
    }

    [Fact]
    public void WrongPassword_OrAFileThatIsNotAKey_ShouldBeRefused()
    {
        var (cer, key) = Make();

        Assert.Equal(SealingCertificateReader.WrongPasswordMessage, SealingCertificateReader.Read(cer, key, "otra").Error);
        Assert.Equal(SealingCertificateReader.WrongPasswordMessage, SealingCertificateReader.Read(cer, cer, Password).Error);
        Assert.Equal(SealingCertificateReader.WrongPasswordMessage, SealingCertificateReader.Read(cer, key, "").Error);
    }

    [Fact]
    public void KeyOfAnotherCertificate_ShouldBeRefused()
    {
        var (cer, _) = Make();
        var (_, otherKey) = Make();

        Assert.Equal(SealingCertificateReader.KeyMismatchMessage, SealingCertificateReader.Read(cer, otherKey, Password).Error);
    }

    [Fact]
    public void FileThatIsNotACertificate_ShouldBeRefused()
    {
        var (_, key) = Make();

        Assert.Equal(SealingCertificateReader.NotACertificateMessage,
            SealingCertificateReader.Read(Encoding.UTF8.GetBytes("no soy un certificado"), key, Password).Error);
        Assert.Equal(SealingCertificateReader.NotACertificateMessage, SealingCertificateReader.Read([], key, Password).Error);
        Assert.Equal(SealingCertificateReader.NotACertificateMessage,
            SealingCertificateReader.Read(new byte[SealingCertificateReader.MaxFileBytes + 1], key, Password).Error);
    }

    [Fact]
    public void CertificateWithoutAnRfc_ShouldBeRefused()
    {
        var (cer, key) = Make(uniqueIdentifier: null);

        Assert.Equal(SealingCertificateReader.NoRfcMessage, SealingCertificateReader.Read(cer, key, Password).Error);
    }

    [Fact]
    public void CertificateWhoseSerialIsNotASatNumber_ShouldBeRefused()
    {
        var (cer, key) = Make(serial: [0x01, 0x02, 0x03]);

        Assert.Equal(SealingCertificateReader.NotACertificateMessage, SealingCertificateReader.Read(cer, key, Password).Error);
    }

    /// <summary>A certificate as the SAT issues it, and its key encrypted with the password as a .key file.</summary>
    private static (byte[] Cer, byte[] Key) Make(
        string? uniqueIdentifier = "EKU9003173C9 / VADA800927HSRSRL05",
        X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
        byte[]? serial = null)
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
            name, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1), From, To, serial ?? Encoding.ASCII.GetBytes(Number));

        var key = rsa.ExportEncryptedPkcs8PrivateKey(
            Encoding.UTF8.GetBytes(Password), new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048));
        return (certificate.Export(X509ContentType.Cert), key);
    }
}
