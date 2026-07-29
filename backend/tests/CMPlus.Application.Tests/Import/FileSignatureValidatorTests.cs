using System.Text;
using CMPlus.Application.Import;

namespace CMPlus.Application.Tests.Import;

/// <summary>
/// S3-SEC-01 DoD item 2: pure unit tests for the magic-byte gate itself, independent of which
/// command handler calls it - see the handler test classes for the "posted to the wrong route"
/// integration-shaped coverage.
/// </summary>
public class FileSignatureValidatorTests
{
    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Theory]
    [InlineData("ERMHDR\t18.8\t2026-01-01\tProject")]
    [InlineData("%T\tPROJWBS\n%F\twbs_id\n%R\tW1")]
    public void IsXer_Accepts_Both_Shapes_XerDocumentReader_Itself_Tolerates(string text) =>
        Assert.True(FileSignatureValidator.IsXer(Utf8(text)));

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><Project></Project>")]
    [InlineData("not xer or xml or zip at all")]
    public void IsXer_Rejects_Non_Xer_Content(string text) =>
        Assert.False(FileSignatureValidator.IsXer(Utf8(text)));

    [Fact]
    public void IsXer_Rejects_An_Xlsx_Zip_Signature()
    {
        byte[] xlsxBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00];
        Assert.False(FileSignatureValidator.IsXer(xlsxBytes));
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Project></Project>")]
    [InlineData("<Project xmlns=\"http://schemas.microsoft.com/project\"></Project>")] // a hand-built fixture may omit the declaration.
    public void IsMspdi_Accepts_Both_Shapes(string text) =>
        Assert.True(FileSignatureValidator.IsMspdi(Utf8(text)));

    [Theory]
    [InlineData("ERMHDR\t18.8\t2026-01-01\tProject")]
    [InlineData("not xer or xml or zip at all")]
    public void IsMspdi_Rejects_Non_Xml_Content(string text) =>
        Assert.False(FileSignatureValidator.IsMspdi(Utf8(text)));

    [Fact]
    public void IsMspdi_Rejects_An_Xlsx_Zip_Signature()
    {
        byte[] xlsxBytes = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00];
        Assert.False(FileSignatureValidator.IsMspdi(xlsxBytes));
    }

    [Fact]
    public void IsXlsx_Accepts_The_Zip_Local_File_Header_Signature()
    {
        byte[] xlsxBytes = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];
        Assert.True(FileSignatureValidator.IsXlsx(xlsxBytes));
    }

    [Theory]
    [InlineData("ERMHDR\t18.8\t2026-01-01\tProject")]
    [InlineData("<?xml version=\"1.0\"?><Project></Project>")]
    public void IsXlsx_Rejects_Non_Zip_Content(string text) =>
        Assert.False(FileSignatureValidator.IsXlsx(Utf8(text)));

    [Fact]
    public void IsXlsx_Rejects_A_Password_Protected_Ole2_Cfb_Container()
    {
        // Password-protected OOXML is wrapped in an OLE2/Compound-File-Binary container, NOT a
        // plain ZIP - its own distinct magic signature, correctly not a ZIP local-file-header match.
        byte[] ole2CfbBytes = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        Assert.False(FileSignatureValidator.IsXlsx(ole2CfbBytes));
    }

    [Fact]
    public void IsXer_Tolerates_A_Leading_Utf8_Bom()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8("ERMHDR\t18.8")).ToArray();
        Assert.True(FileSignatureValidator.IsXer(withBom));
    }

    [Fact]
    public void IsXlsx_Rejects_Content_Shorter_Than_The_Signature()
    {
        byte[] tooShort = [0x50, 0x4B];
        Assert.False(FileSignatureValidator.IsXlsx(tooShort));
    }
}
