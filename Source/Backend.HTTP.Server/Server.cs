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
using System.Threading;
using System.Threading.Tasks;
using UtilPack;

namespace Backend.HTTP.Server
{

   public class Server
   {

      private readonly AuthenticatorAggregator<HttpRequest, HttpContext> _authChecker;
      private readonly (ResponseCreatorMatcher<HttpRequest>, ResponseCreator<HttpRequest, HttpContext>)[] _responseCreators;

      public Server(
         AuthenticatorAggregator<HttpRequest, HttpContext> authChecker,
         (ResponseCreatorMatcher<HttpRequest>, ResponseCreator<HttpRequest, HttpContext>)[] responseCreators,
         String guestUserID = null
         )
      {
         this._responseCreators = responseCreators ?? Array.Empty<(ResponseCreatorMatcher<HttpRequest>, ResponseCreator<HttpRequest, HttpContext>)>();
         this._authChecker = authChecker ?? new HTTPAuthenticatorAggregator( null );
      }

      public async Task ProcessRequest( HttpContext context )
      {
         // TODO make it possible to create one big regex which would identify the creator right away.
         try
         {
            var creator = this._responseCreators
               .Select( curCreator =>
               {
                  var info = curCreator.Item1.IsMatch( context.Request, out var wasMatch );
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
            }
         }
         catch ( Exception exc )
         {
            if ( !context.RequestAborted.IsCancellationRequested )
            {
               Console.Error.WriteLine( "Exception when processing request.\n{0}", exc );
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

         var loggerFactory = new ConsoleErrorLoggerProvider();
         var hostBuilder = new WebHostBuilder();

         var creationParams = new ResponseCreatorInstantiationParameters(
            configurationLocation,
            hostBuilder
            );

         var creators = await Task.WhenAll(
            runningConfiguration.ResponseCreatorFactories
               .Select( t => t.CreateResponseCreatorAsync( creationParams, token ).AsTask() )
               .ToArray()
            );

         var server = new Server(
            runningConfiguration.AuthChecker,
            creators
            );

         hostBuilder
            .UseLoggerFactory( loggerFactory )
            .UseKestrel( options =>
            {
               // Run callback for options
               runningConfiguration.ServerOptionsProcessor?.Invoke( options );

               // Run callback for limits
               runningConfiguration.ServerLimitsProcessor?.Invoke( options.Limits );

               // Disable the server info (which will be "Kestrel")
               options.AddServerHeader = false;

               // Configure https
               options.UseHttps( new Microsoft.AspNetCore.Server.Kestrel.Https.HttpsConnectionFilterOptions()
               {
                  ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.NoCertificate,
                  ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2( runningConfiguration.CertificateFile, runningConfiguration.CertificatePassword ),
                  SslProtocols = System.Security.Authentication.SslProtocols.Tls12
               } );
            } )
            .Configure( app =>
            {
               creationParams.OnConfigureEventValue.InvokeAllEventHandlers( del => del( app ), throwExceptions: false );
               app.Use( del =>
               {
                  return server.ProcessRequest;
               } );
            } )
            .UseUrls( runningConfiguration.URLs.Select( url => "https://" + url ).ToArray() )
            ;
         return hostBuilder.Build();

      }
   }

   public interface ServerConfiguration
   {
      String CertificateFile { get; }

      String CertificatePassword { get; }

      Action<Microsoft.AspNetCore.Server.Kestrel.KestrelServerOptions> ServerOptionsProcessor { get; }

      Action<Microsoft.AspNetCore.Server.Kestrel.KestrelServerLimits> ServerLimitsProcessor { get; }

      String[] URLs { get; }

      AuthenticatorAggregator<HttpRequest, HttpContext> AuthChecker { get; }

      ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>[] ResponseCreatorFactories { get; }
   }

   public class ServerConfigurationImpl : ServerConfiguration
   {

      public ServerConfigurationImpl(
         String certificateFile,
         String certificatePassword,
         Action<Microsoft.AspNetCore.Server.Kestrel.KestrelServerOptions> serverOptionsProcessor,
         Action<Microsoft.AspNetCore.Server.Kestrel.KestrelServerLimits> serverLimitsProcessor,
         String[] urls,
         AuthenticatorAggregator<HttpRequest, HttpContext> authChecker,
         ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>[] responseCreators
         )
      {
         this.CertificateFile = certificateFile;
         this.CertificatePassword = certificatePassword;
         this.ServerOptionsProcessor = serverOptionsProcessor;
         this.ServerLimitsProcessor = serverLimitsProcessor;
         this.URLs = urls;
         this.AuthChecker = authChecker;
         this.ResponseCreatorFactories = responseCreators;
      }

      public String CertificateFile { get; }

      public String CertificatePassword { get; }

      public Action<Microsoft.AspNetCore.Server.Kestrel.KestrelServerOptions> ServerOptionsProcessor { get; }

      public Action<Microsoft.AspNetCore.Server.Kestrel.KestrelServerLimits> ServerLimitsProcessor { get; }

      public String[] URLs { get; }

      public AuthenticatorAggregator<HttpRequest, HttpContext> AuthChecker { get; }

      public ResponseCreatorFactory<HttpRequest, HttpRequest, HttpContext, ResponseCreatorInstantiationParameters>[] ResponseCreatorFactories { get; }
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