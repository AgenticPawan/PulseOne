using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PulseOne.Application.Behaviors;

namespace PulseOne.Application;

/// <summary>
/// Composition of the Application layer's CQRS pipeline (blueprint §6.2): MediatR plus the
/// ordered set of cross-cutting pipeline behaviors and every FluentValidation validator in the
/// assembly. Called once from the host composition root.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var assembly = typeof(AssemblyMarker).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order is load-bearing. MediatR runs open behaviors in registration order, so:
            //   log → validate → transaction → handler.
            // Validation runs before the transaction, so an invalid request never opens one.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        // Picks up every IValidator<T> in the assembly so ValidationBehavior can resolve them.
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
