/*
 * Copyright 2017 Stanislav Muhametsin. All rights Reserved.
 *
 * Licensed  under the  Apache License,  Version 2.0  (the "License");
 * you may not use  this file  except in  compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed  under the  License is distributed on an "AS IS" BASIS,
 * WITHOUT  WARRANTIES OR CONDITIONS  OF ANY KIND, either  express  or
 * implied.
 *
 * See the License for the specific language governing permissions and
 * limitations under the License. 
 */
using Backend.Core;
using Backend.HTTP.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using UtilPack;
using UtilPack.Logging;
using TLogger = UtilPack.Logging.Publish.LogPublisher<Backend.HTTP.Common.HttpLogInfo>;
using TLoggerFactory = UtilPack.Logging.Consume.LogConsumerFactory<Backend.HTTP.Common.HttpLogInfo>;

namespace Backend.HTTP.Server
{

   public class Server
   {

      private readonly AuthenticatorAggregator<HttpRequest, HttpContext> _authChecker;
      private readonly (ResponseCreatorMatcher<HttpRequest>, ResponseCreator<HttpRequest, HttpContext>)[] _responseCreators;
      private readonly TLogger _logger;

      public Server(
         TLogger logger,
         AuthenticatorAggregator<HttpRequest, HttpContext> authChecker,
         (ResponseCreatorMatcher<HttpRequest>, ResponseCreator<HttpRequest, HttpContext>)[] responseCreators,
         String guestUserID = null
         )
      {
         this._logger = logger;
         this._responseCreators = responseCreators ?? Array.Empty<(ResponseCreatorMatcher<HttpRequest>, ResponseCreator<HttpRequest, HttpContext>)>();
         this._authChecker = authChecker ?? new HTTPAuthenticatorAggregator( null );
      }

      public async Task ProcessRequest( HttpContext context )
      {
         // TODO make it possible to create one big regex which would identify the creator right away.
         var request = context.Request;
         try
         {
            var creator = this._responseCreators
               .Select( curCreator =>
               {
                  var info = curCreator.Item1.IsMatch( request, out var wasMatch );
                  return (curCreator.Item2, wasMatch, info);
               } ).FirstOrDefault( curCreator => curCreator.Item2 );

            if ( !creator.Item2 )
            {
               context.Response.StatusCode = 400;
               await context.Response.WriteAsync( "" );
            }
            else
            {
               var task = creator.Item1?.ProcessForResponseAsync( creator.Item3, context, this._authChecker );
               if ( task != null )
               {
                  await task;
               }

               this._logger.Publish( HttpLogInfo.Trace, "Processed request {0} with method {1} and response code {2}.", request.Path, request.Method, context.Response.StatusCode );
            }
         }
         catch ( Exception exc )
         {
            if ( !context.RequestAborted.IsCancellationRequested )
            {
               await this._logger.PublishAsync( HttpLogInfo.Error, "Error when processing request for {0}: {1}", request.Path, exc.Message );
               try
               {
                  context.Response.StatusCode = 500;
                  await context.Response.WriteAsync( "" );
               }
               catch
               {
                  // Just ignore this one...
               }
            }
         }
      }

      public static async Task<IWebHost> CreateServerAsync(
         String configurationLocation,
         ServerConfiguration runningConfiguration,
         CancellationToken token
         )
      {
         if ( runningConfiguration == null )
         {
            throw new ArgumentNullException( nameof( runningConfiguration ) );
         }

         var hostBuilder = new WebHostBuilder();
         var loggerContext = new UtilPack.Logging.Bootstrap.LogRegistration<HttpLogInfo>();
         loggerContext.RegisterLoggers( runningConfiguration.Loggers );
         var logger = loggerContext.CreatePublisherFromCurrentRegistrations();

         var creationParams = new ResponseCreatorInstantiationParameters(
            configurationLocation,
            hostBuilder,
            logger
            );

         var creators = await Task.WhenAll(
            runningConfiguration.ResponseCreatorFactories
               .Select( t => t.CreateResponseCreatorAsync( creationParams, token ) )
               .ToArray()
            );

         var server = new Server(
            logger,
            runningConfiguration.AuthChecker,
            creators
            );

         hostBuilder
            .ConfigureLogging( ctx => new ConsoleErrorLoggerProvider() )
            .UseKestrel( options =>
            {
               // Run callback for options
               runningConfiguration.ServerOptionsProcessor?.Invoke( options );

               // Run callback for limits
               runningConfiguration.ServerLimitsProcessor?.Invoke( options.Limits );

               // Disable the server info (which will be "Kestrel")
               options.AddServerHeader = false;
               options.AllowSynchronousIO = false;

               // Configure endpoints
               foreach ( var kvp in runningConfiguration.EndPoints )
               {
                  var ep = kvp.Key;
                  var epConfig = kvp.Value;
                  var certFile = epConfig.CertificateFile;
                  if ( String.IsNullOrEmpty( certFile ) )
                  {
                     options.Listen( ep );
                  }
                  else
                  {
                     options.Listen( ep, listenOptions =>
                     {
                        listenOptions.UseHttps( new Microsoft.AspNetCore.Server.Kestrel.Https.HttpsConnectionAdapterOptions()
                        {
                           CheckCertificateRevocation = epConfig.CheckCertificateRevocation,
                           ClientCertificateValidation = epConfig.ClientCertificateValidation,
                           ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.NoCertificate,
                           ServerCertificate = new X509Certificate2( certFile, epConfig.CertificatePassword ),
                           SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                        } );
                     } );
                  }
               }
            } )
            .Configure( app =>
            {
               creationParams.OnConfigureEventValue.InvokeAllEventHandlers( del => del( app ), throwExceptions: false );
               app.Use( del =>
               {
                  return server.ProcessRequest;
               } );
            } )
            ;
         return hostBuilder.Build();

      }
   }

   public interface ServerConfiguration
   {

      Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions> ServerOptionsProcessor { get; }

      Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerLimits> ServerLimitsProcessor { get; }

      IReadOnlyDictionary<IPEndPoint, ServerEndPointConfiguration> EndPoints { get; }

      AuthenticatorAggregator<HttpRequest, HttpContext> AuthChecker { get; }

      ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>[] ResponseCreatorFactories { get; }

      IReadOnlyList<TLoggerFactory> Loggers { get; }
   }

   public interface ServerEndPointConfiguration
   {
      String CertificateFile { get; }

      String CertificatePassword { get; }

      Boolean CheckCertificateRevocation { get; }

      Func<X509Certificate2, X509Chain, SslPolicyErrors, Boolean> ClientCertificateValidation { get; }
   }

   public class ServerConfigurationImpl : ServerConfiguration
   {

      public ServerConfigurationImpl(
         Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions> serverOptionsProcessor,
         Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerLimits> serverLimitsProcessor,
         IReadOnlyDictionary<IPEndPoint, ServerEndPointConfiguration> endPoints,
         AuthenticatorAggregator<HttpRequest, HttpContext> authChecker,
         ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>[] responseCreators,
         IReadOnlyList<TLoggerFactory> logger
         )
      {
         this.ServerOptionsProcessor = serverOptionsProcessor;
         this.ServerLimitsProcessor = serverLimitsProcessor;
         this.EndPoints = ArgumentValidator.ValidateNotNull( nameof( endPoints ), endPoints );
         this.AuthChecker = authChecker;
         this.ResponseCreatorFactories = responseCreators;
         this.Loggers = ArgumentValidator.ValidateNotNull( nameof( logger ), logger );
      }


      public Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions> ServerOptionsProcessor { get; }

      public Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerLimits> ServerLimitsProcessor { get; }

      public IReadOnlyDictionary<IPEndPoint, ServerEndPointConfiguration> EndPoints { get; }

      public AuthenticatorAggregator<HttpRequest, HttpContext> AuthChecker { get; }

      public ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>[] ResponseCreatorFactories { get; }

      public IReadOnlyList<TLoggerFactory> Loggers { get; }
   }

   public class ServerEndPointConfigurationImpl : ServerEndPointConfiguration
   {
      public ServerEndPointConfigurationImpl(
         String certificateFile,
         String certificatePassword,
         Boolean checkCertificateRevocation,
         Func<X509Certificate2, X509Chain, SslPolicyErrors, Boolean> clientCertificateValidation
         )
      {
         this.CertificateFile = certificateFile;
         this.CertificatePassword = certificatePassword;
         this.CheckCertificateRevocation = checkCertificateRevocation;
         this.ClientCertificateValidation = clientCertificateValidation;
      }

      public String CertificateFile { get; }

      public String CertificatePassword { get; }

      public Boolean CheckCertificateRevocation { get; }

      public Func<X509Certificate2, X509Chain, SslPolicyErrors, Boolean> ClientCertificateValidation { get; }
   }

   internal sealed class ConsoleErrorLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider, Microsoft.Extensions.Logging.ILoggerFactory
   {
      private readonly ConcurrentDictionary<String, Microsoft.Extensions.Logging.ILogger> _loggers;

      public ConsoleErrorLoggerProvider()
      {
         this._loggers = new ConcurrentDictionary<String, Microsoft.Extensions.Logging.ILogger>();
      }

      private sealed class ConsoleErrorLogger : Microsoft.Extensions.Logging.ILogger
      {
         public IDisposable BeginScope<TState>( TState state )
         {
            return null;
         }

         public Boolean IsEnabled( Microsoft.Extensions.Logging.LogLevel logLevel )
         {
            return logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning;
         }

         public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, String> formatter
            )
         {
            // Only log warnings or more sever events
            if ( logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning )
            {
               Console.Error.WriteLine( formatter( state, exception ) );
            }
         }
      }

      public Microsoft.Extensions.Logging.ILogger CreateLogger( String categoryName )
      {
         return this._loggers.GetOrAdd( categoryName, cat => new ConsoleErrorLogger() );
      }

      public void Dispose()
      {
         this._loggers.Clear();
      }

      public void AddProvider( Microsoft.Extensions.Logging.ILoggerProvider provider )
      {
         // Don't do anything.
      }
   }



}
