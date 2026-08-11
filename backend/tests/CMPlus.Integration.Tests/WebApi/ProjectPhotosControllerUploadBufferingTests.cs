using CMPlus.Application.Abstractions;
using CMPlus.WebApi.Controllers.Photos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S12-BE-01 DoD, verbatim: "enforce the oversize limit before buffering the whole thing into
/// memory". A full-HTTP test (<c>CustomWebApplicationFactory</c> + <c>HttpClient</c>) cannot prove
/// this - however oversized the payload, the real bytes must physically arrive over the wire before
/// ASP.NET Core can even construct an <c>IFormFile</c> for the test to send, so "was it buffered
/// server-side into a second in-memory copy" is invisible from outside the process. This test
/// instead calls <see cref="ProjectPhotosController.Upload"/> directly with a poison
/// <see cref="IFormFile"/> whose <see cref="IFormFile.CopyToAsync"/>/<see cref="IFormFile.CopyTo"/>/
/// <see cref="IFormFile.OpenReadStream"/> all throw - proving the controller's oversize branch never
/// reaches any of the methods that would copy the file's bytes into memory. <c>ISender</c> is
/// deliberately passed as <see langword="null"/> too: the correct oversize path never reaches
/// <c>sender.Send(...)</c> either, so a null-reference would surface exactly as loudly as the poison
/// stream throwing if a future change moved the size check after either operation.
/// </summary>
public class ProjectPhotosControllerUploadBufferingTests
{
    private sealed class TinyCapPhotoOptionsProvider : IPhotoOptionsProvider
    {
        public long MaxFileSizeBytes => 1_000; // 1 KB - the poison file below declares far more.
    }

    /// <summary>Declares a large <see cref="Length"/> (as the real multipart parser would from the
    /// part's Content-Length) but throws on every method that would actually read/copy its bytes -
    /// the real oversized content is never constructed at all, mirroring how the production
    /// controller path avoids materializing it.</summary>
    private sealed class PoisonFormFile : IFormFile
    {
        public string ContentType => "image/jpeg";
        public string ContentDisposition => "form-data; name=\"file\"; filename=\"huge.jpg\"";
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => 50_000_000; // 50 MB - far over the 1 KB cap.
        public string Name => "file";
        public string FileName => "huge.jpg";

        public Stream OpenReadStream() =>
            throw new InvalidOperationException("Must not open the stream of an already-known-oversized upload.");

        public void CopyTo(Stream target) =>
            throw new InvalidOperationException("Must not buffer an already-known-oversized upload into memory.");

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not buffer an already-known-oversized upload into memory.");
    }

    [Fact]
    public async Task Upload_Rejects_An_Oversized_File_Without_Ever_Touching_Its_Bytes_Or_Calling_MediatR()
    {
        var controller = new ProjectPhotosController(sender: null!, new TinyCapPhotoOptionsProvider())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Upload(
            Guid.NewGuid(), new PoisonFormFile(), activityId: null, caption: null, capturedAt: null, CancellationToken.None);

        // Reaching this line at all (rather than the poison IFormFile's InvalidOperationException,
        // or a NullReferenceException off the null ISender) is the proof - see this class's remarks.
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("https://cmplus.dev/problems/photo-file-too-large", problem.Type);
    }
}
