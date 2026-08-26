using System;
using System.IO;
using System.Threading.Tasks;
using FicheGen.Core.Abstractions;
using FicheGen.Infrastructure.Ocr;
using FicheGen.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace FicheGen.Infrastructure.Tests.Ocr;

public class WindowsNativeOcrServiceTests
{
    [Fact]
    public void OcrService_Instantiates_AndReportsSupportStatus()
    {
        var ocr = new WindowsNativeOcrService();
        ocr.Should().NotBeNull();
        // On modern Windows 10/11 machines, IsSupported is true when French/English OCR packs exist
        ocr.Invoking(o => _ = o.IsSupported).Should().NotThrow();
    }

    [Fact]
    public async Task ExtractTextFromImageFile_WithNonExistentFile_ReturnsEmpty()
    {
        var ocr = new WindowsNativeOcrService();
        var result = await ocr.ExtractTextFromImageFileAsync(@"C:\non_existent_file_xyz.png");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTextFromPdfPage_WithNonExistentFile_ReturnsEmpty()
    {
        var ocr = new WindowsNativeOcrService();
        var result = await ocr.ExtractTextFromPdfPageAsync(@"C:\non_existent_file_xyz.pdf", 1);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTextFromImageBytes_WithEmptyArray_ReturnsEmpty()
    {
        var ocr = new WindowsNativeOcrService();
        var result = await ocr.ExtractTextFromImageBytesAsync(Array.Empty<byte>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void PdfGuideService_AcceptsOcrServiceInConstructor()
    {
        var ocr = new WindowsNativeOcrService();
        var guideService = new PdfGuideService(ocrService: ocr);
        guideService.Should().NotBeNull();
    }
}
