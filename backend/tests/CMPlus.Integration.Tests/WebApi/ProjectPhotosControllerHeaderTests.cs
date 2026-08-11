using CMPlus.Application.Abstractions;
using CMPlus.Application.Features.Photos.Queries.GetPhotoContent;
using CMPlus.Domain.Common;
using CMPlus.WebApi.Controllers.Photos;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// L-05 (security review sprint-12.md): <c>ProjectPhotosController.Get</c> must set
/// <c>X-Content-Type-Options</c> with an INDEXER, not <c>Headers.Append</c> - <c>Append</c> adds a
/// second value ("nosniff,nosniff") the moment anything upstream (a future global security-header
/// middleware, or - as reproduced here - anything else already populating the response) has already
/// set the same header. A real end-to-end HTTP test cannot reproduce this today (this API sets no
/// global header middleware yet, so <c>Headers</c> starts empty on every real request) - this test
/// instead calls the controller action directly against an <see cref="HttpContext"/> whose response
/// already carries the header, the same technique
/// <c>ProjectPhotosControllerUploadBufferingTests</c> uses to prove a buffering property no full-HTTP
/// test can observe.
/// </summary>
public class ProjectPhotosControllerHeaderTests
{
    /// <summary>Returns a fixed, successful <c>GetPhotoContentQuery</c> result for every call - only
    /// the one <c>Send&lt;TResponse&gt;</c> overload the controller actually calls is meaningfully
    /// implemented.</summary>
    private sealed class FakeSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var result = Result<PhotoContentDto>.Success(new PhotoContentDto([1, 2, 3], "image/jpeg", "photo.jpg"));
            return Task.FromResult((TResponse)(object)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException("Not exercised by this test.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by this test.");
    }

    private sealed class FixedPhotoOptionsProvider : IPhotoOptionsProvider
    {
        public long MaxFileSizeBytes => 10 * 1024 * 1024;
    }

    [Fact]
    public async Task Get_Sets_The_NoSniff_Header_Exactly_Once_Even_When_The_Response_Already_Carries_It()
    {
        var httpContext = new DefaultHttpContext();
        // Simulates a header already present on the response before this action runs - what a
        // future global security-header middleware (tracked separately, sprint-10.md L-06) would do.
        // Headers.Append (the pre-fix code) would turn this into "nosniff,nosniff"; the indexer
        // assignment this fix uses must instead leave exactly one value.
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var controller = new ProjectPhotosController(new FakeSender(), new FixedPhotoOptionsProvider())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };

        var result = await controller.Get(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<FileContentResult>(result);
        var headerValues = httpContext.Response.Headers["X-Content-Type-Options"];
        Assert.Single(headerValues);
        Assert.Equal("nosniff", headerValues[0]);
    }
}
