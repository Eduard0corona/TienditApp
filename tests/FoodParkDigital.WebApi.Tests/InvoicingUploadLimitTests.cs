using System.Reflection;
using System.Text.Json;
using FoodParkDigital.Application.Invoicing;
using FoodParkDigital.WebApi.Controllers;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace FoodParkDigital.WebApi.Tests;

/// <summary>
/// The CSD upload (Fase 28A) takes two files of a few KB: its body is capped before it is read, so a JSON of the default
/// ~30 MB never reaches the binding or the reader.
/// </summary>
public class InvoicingUploadLimitTests
{
    [Fact]
    public void CertificateUpload_ShouldTakeTheLargestCsdTheReaderReads_AndNothingMuchLarger()
    {
        var limit = typeof(InvoicingController).GetMethod(nameof(InvoicingController.UploadCertificate))!
            .GetCustomAttribute<RequestSizeLimitAttribute>() as IRequestSizeLimitMetadata;
        var largest = JsonSerializer.SerializeToUtf8Bytes(new UploadInvoicingCertificateRequest
        {
            Certificate = Convert.ToBase64String(new byte[SealingCertificateReader.MaxFileBytes]),
            PrivateKey = Convert.ToBase64String(new byte[SealingCertificateReader.MaxFileBytes]),
            Password = new string('ñ', SealingCertificateReader.MaxPasswordLength)
        });

        Assert.Equal(64 * 1024, limit?.MaxRequestBodySize);
        Assert.InRange(largest.Length, 1, limit!.MaxRequestBodySize!.Value);
    }
}
