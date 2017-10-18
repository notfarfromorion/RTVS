﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Common.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Lifetime;
using Microsoft.R.Host.Broker.Logging;
using Microsoft.R.Host.Broker.RemoteUri;
using Microsoft.R.Host.Broker.Security;
using Microsoft.R.Host.Broker.Sessions;
using Microsoft.R.Host.Protocol;
using Newtonsoft.Json;

namespace Microsoft.R.Host.Broker.Startup {
    public class Startup {
        public static IConfigurationRoot LoadConfiguration(ILoggerFactory loggerFactory, string configPath, string[] args) {
            var configuration = new ConfigurationBuilder().AddCommandLine(args);

            if (configPath != null) {
                try {
                    configuration.AddJsonFile(configPath, optional: false);
                } catch (IOException ex) {
                    loggerFactory.CreateLogger<Startup>()
                        .LogCritical(Resources.Error_ConfigLoadFailed, ex.Message);
                    Environment.Exit((int)BrokerExitCodes.BadConfigFile);
                } catch (JsonException ex) {
                    loggerFactory.CreateLogger<Startup>()
                        .LogCritical(Resources.Error_ConfigParseFailed, ex.Message);
                    Environment.Exit((int)BrokerExitCodes.BadConfigFile);
                }
            }

            return configuration.Build();
        }

        private ILogger<Startup> _logger;
        protected ILoggerFactory LoggerFactory { get; }
        protected IConfigurationRoot Configuration { get; }

        protected Startup(ILoggerFactory loggerFactory, IConfigurationRoot configuration) {
            LoggerFactory = loggerFactory;
            Configuration = configuration;
        }

        public virtual void ConfigureServices(IServiceCollection services) {
            services
                .AddOptions()
                .Configure<LifetimeOptions>(Configuration.GetLifetimeSection())
                .Configure<LoggingOptions>(Configuration.GetLoggingSection())
                .Configure<ROptions>(Configuration.GetRSection())
                .Configure<SecurityOptions>(Configuration.GetSecuritySection())
                .Configure<StartupOptions>(Configuration.GetStartupSection())

                .AddSingleton<LifetimeManager>()
                .AddSingleton<SecurityManager>()
                .AddSingleton<InterpreterManager>()
                .AddSingleton<SessionManager>()

                .AddAuthorization(options => options.AddPolicy(
                    Policies.RUser,
                    policy => policy.RequireClaim(Claims.RUser)))

                .AddRouting()
                .AddMvc()
                .AddApplicationPart(typeof(Startup).GetTypeInfo().Assembly);
        }

        public virtual void Configure(IApplicationBuilder app
            , IApplicationLifetime applicationLifetime
            , IHostingEnvironment env
            , IOptions<StartupOptions> startupOptions
            , ILogger<Startup> logger
            , LifetimeManager lifetimeManager
            , InterpreterManager interpreterManager
            , SecurityManager securityManager) {

            _logger = logger;
            var serverAddresses = app.ServerFeatures.Get<IServerAddressesFeature>();
            var pipeName = startupOptions.Value.WriteServerUrlsToPipe;
            if (pipeName != null) {
                NamedPipeClientStream pipe;
                try {
                    pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    pipe.Connect(Debugger.IsAttached ? 200000 : 10000);
                } catch (IOException ex) {
                    logger.LogCritical(0, ex, Resources.Critical_InvalidPipeHandle, pipeName);
                    throw;
                } catch (TimeoutException ex) {
                    logger.LogCritical(0, ex, Resources.Critical_PipeConnectTimeOut, pipeName);
                    throw;
                }

                applicationLifetime.ApplicationStarted.Register(() => Task.Run(() => {
                    using (pipe) {
                        var serverUriStr = JsonConvert.SerializeObject(serverAddresses.Addresses);
                        logger.LogTrace(Resources.Trace_ServerUrlsToPipeBegin, pipeName, Environment.NewLine, serverUriStr);

                        var serverUriData = Encoding.UTF8.GetBytes(serverUriStr);
                        pipe.Write(serverUriData, 0, serverUriData.Length);
                        pipe.Flush();
                    }

                    logger.LogTrace(Resources.Trace_ServerUrlsToPipeDone, pipeName);
                }));
            }

            lifetimeManager.Initialize();
            interpreterManager.Initialize();

            app.UseWebSockets(new WebSocketOptions {
                ReplaceFeature = true,
                KeepAliveInterval = TimeSpan.FromMilliseconds(1000000000),
                ReceiveBufferSize = 0x10000
            });

            var routeBuilder = new RouteBuilder(app, new RouteHandler(RemoteUriHelper.HandlerAsync));
            routeBuilder.MapRoute("help_and_shiny", "remoteuri");
            app.UseRouter(routeBuilder.Build());

            app.Use((context, next) => context.User.Identity.IsAuthenticated 
                ? next() 
                : context.AuthenticateAsync());

            app.UseMvc();
            app.UseAuthentication();

            if (!startupOptions.Value.IsService) {
                applicationLifetime.ApplicationStopping.Register(ExitAfterTimeout);
            }
        }

        private void ExitAfterTimeout() => ExitAfterTimeoutAsync().DoNotWait();

        private async Task ExitAfterTimeoutAsync() {
            await Task.Delay(10000);
            _logger.LogCritical(Resources.Critical_TimeOutShutdown);
            Environment.Exit((int)BrokerExitCodes.Timeout);
        }
    }
}
