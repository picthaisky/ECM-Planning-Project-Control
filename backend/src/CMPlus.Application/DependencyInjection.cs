using CMPlus.Application.Common;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CMPlus.Application;

/// <summary>Composition root helper for Application-layer registrations (S2-BE-01): MediatR
/// handlers, FluentValidation validators, and the validation pipeline behavior - all discovered
/// from this assembly, so a new Feature folder needs no DI wiring of its own.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var applicationAssembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
        services.AddValidatorsFromAssembly(applicationAssembly);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddSingleton<Approval.IApprovalRoutingService, Approval.ApprovalRoutingService>();

        return services;
    }
}
