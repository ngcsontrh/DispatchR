using DispatchR.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using DispatchR.Abstractions.Notification;
using DispatchR.Abstractions.Send;
using DispatchR.Abstractions.Stream;
using DispatchR.Exceptions;

namespace DispatchR.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDispatchR(this IServiceCollection services, Action<ConfigurationOptions> configuration)
    {
        var config = new ConfigurationOptions();
        configuration(config);

        if (config is {IncludeHandlers.Count:0})
        {
            throw new IncludeHandlersCannotBeArrayEmptyException();
        }
        
        if (config is {ExcludeHandlers.Count:0})
        {
            throw new ExcludeHandlersCannotBeArrayEmptyException();
        }

        return services.AddDispatchR(config);
    }

    public static IServiceCollection AddDispatchR(this IServiceCollection services, Assembly assembly, bool withPipelines = true, bool withNotifications = true)
    {
        var config = new ConfigurationOptions
        {
            RegisterPipelines = withPipelines,
            RegisterNotifications = withNotifications
        };

        config.Assemblies.Add(assembly);

        return services.AddDispatchR(config);
    }

    public static IServiceCollection AddDispatchR(this IServiceCollection services, ConfigurationOptions configurationOptions)
    {
        services.AddScoped<IMediator, Mediator>();
        var requestHandlerType = typeof(IRequestHandler<,>);
        var pipelineBehaviorType = typeof(IPipelineBehavior<,>);
        var streamRequestHandlerType = typeof(IStreamRequestHandler<,>);
        var streamPipelineBehaviorType = typeof(IStreamPipelineBehavior<,>);
        var syncNotificationHandlerType = typeof(INotificationHandler<>);

        var otherHandlerTypes = new HashSet<Type>()
        {
            pipelineBehaviorType,
            streamPipelineBehaviorType,
            syncNotificationHandlerType
        };

        var allTypes = configurationOptions.Assemblies.SelectMany(x => x.GetTypes()).Distinct()
            .Where(type =>
            {
                var genericInterfaces = type.GetInterfaces()
                    .Where(i => i.IsGenericType)
                    .Select(i => i.GetGenericTypeDefinition())
                    .ToList();

                var isRequestHandler = genericInterfaces.Contains(requestHandlerType);
                var isPipelineBehavior = genericInterfaces.Contains(pipelineBehaviorType);
                var isStreamRequestHandler = genericInterfaces.Contains(streamRequestHandlerType);
                var isStreamPipelineBehavior = genericInterfaces.Contains(streamPipelineBehaviorType);

                var isConcreteRequestHandler = isRequestHandler && !isPipelineBehavior;
                var isConcreteStreamRequestHandler = isStreamRequestHandler && !isStreamPipelineBehavior;
                if (isConcreteRequestHandler || isConcreteStreamRequestHandler)
                {
                    return configurationOptions.IsHandlerIncluded(type);
                }

                return genericInterfaces.Intersect(otherHandlerTypes).Any();
            })
            .ToList();

        if (configurationOptions.RegisterNotifications)
        {
            ServiceRegistrator.RegisterNotification(services, allTypes, syncNotificationHandlerType);
        }

        ServiceRegistrator.RegisterHandlers(services, allTypes, requestHandlerType, pipelineBehaviorType,
            streamRequestHandlerType, streamPipelineBehaviorType, configurationOptions.RegisterPipelines, configurationOptions.PipelineOrder);

        return services;
    }
}
