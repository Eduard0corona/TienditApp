using System.Security.Cryptography.X509Certificates;
using System.Text;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.Application.Tests.Support;

namespace FoodParkDigital.Application.Tests;

/// <summary>
/// The CSD a business uploads is checked before it reaches the PAC (Fase 28A, ADR 0045), with certificates made the way the
/// SAT makes them (<see cref="TestCsd"/>).
/// </summary>
public class SealingCertificateReaderTests
{
    private const string Password = TestCsd.Password;
    private const string Number = TestCsd.Number;
    private static readonly DateTimeOffset From = TestCsd.From;
    private static readonly DateTimeOffset To = TestCsd.To;

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

    private static (byte[] Cer, byte[] Key) Make(
        string? uniqueIdentifier = "EKU9003173C9 / VADA800927HSRSRL05",
        X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation,
        byte[]? serial = null)
        => TestCsd.Make(uniqueIdentifier, usage, serial);
}
