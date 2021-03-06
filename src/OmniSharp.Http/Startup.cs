using System;
using System.Composition.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OmniSharp.Eventing;
using OmniSharp.Http.Middleware;
using OmniSharp.Options;
using OmniSharp.Roslyn;
using OmniSharp.Services;
using OmniSharp.Stdio.Services;

namespace OmniSharp.Http
{
    class Startup
    {
        private readonly IOmniSharpEnvironment _environment;
        private readonly IEventEmitter _eventEmitter;
        private readonly ISharedTextWriter _writer;
        private readonly IConfigurationRoot _configuration;
        private CompositionHost _compositionHost;

        public Startup(IOmniSharpEnvironment environment, IEventEmitter eventEmitter, ISharedTextWriter writer)
        {
            _environment = environment;
            _eventEmitter = eventEmitter;
            _writer = writer;
            _configuration = new ConfigurationBuilder(environment).Build();
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            var serviceProvider = MefBuilder.CreateDefaultServiceProvider(_configuration, services);
            var mefBuilder = new MefBuilder(serviceProvider, _environment, _writer, _eventEmitter);
            var compositionHost = mefBuilder.Build();
            _compositionHost = compositionHost;
            return serviceProvider;
        }

        public void Configure(
            IApplicationBuilder app,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory,
            IEventEmitter eventEmitter,
            ISharedTextWriter writer,
            HttpEnvironment httpEnvironment,
            IOptionsMonitor<OmniSharpOptions> options)
        {
            var workspace = _compositionHost.GetExport<OmniSharpWorkspace>();
            var logger = loggerFactory.CreateLogger<Startup>();
            loggerFactory.AddConsole((category, level) =>
            {
                if (HostHelpers.LogFilter(category, level, _environment)) return true;

                if (string.Equals(category, typeof(ExceptionHandlerMiddleware).FullName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            });

            app.UseRequestLogging();
            app.UseExceptionHandler("/error");
            app.UseMiddleware<EndpointMiddleware>(_compositionHost);
            app.UseMiddleware<StatusMiddleware>(workspace);
            app.UseMiddleware<StopServerMiddleware>();

            WorkspaceInitializer.Initialize(serviceProvider, _compositionHost, _configuration, logger);

            logger.LogInformation($"Omnisharp server running on port '{httpEnvironment.Port}' at location '{_environment.TargetDirectory}' on host {_environment.HostProcessId}.");
        }
    }
}
