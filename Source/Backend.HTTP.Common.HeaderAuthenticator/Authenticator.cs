using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using UtilPack.Configuration;
using Backend.Core;
using UtilPack;
using UtilPack.Cryptography;
using UtilPack.Cryptography.Digest;
using Backend.HTTP.Common;

namespace Backend.HTTP.Server.HeaderAuthenticator
{

   [ConfigurationType( typeof( HTTPHeaderBasedAuthenticationConfiguration ) )]
   public class HTTPHeaderBasedAuthenticatorFactory : AuthenticatorFactoryImpl
   {
      private readonly HTTPHeaderBasedAuthenticationConfiguration _configuration;

      public HTTPHeaderBasedAuthenticatorFactory(
         HTTPHeaderBasedAuthenticationConfiguration authConfiguration
         )
      {
         this._configuration = authConfiguration ?? new HTTPHeaderBasedAuthenticationConfiguration();
      }

      public override ValueTask<Authenticator<HttpContext, HttpRequest>> CreateAuthenticatorAsync(
         AuthenticationDataHolder creationParameters,
         CancellationToken token
         )
      {
         return new ValueTask<Authenticator<HttpContext, HttpRequest>>( new HTTPHeaderBasedAuthenticationChecker( this._configuration, creationParameters ) );
      }
   }


   public class HTTPHeaderBasedAuthenticationChecker : HTTPAuthenticator
   {

      private readonly String _headerName;

      public HTTPHeaderBasedAuthenticationChecker(
         HTTPHeaderBasedAuthenticationConfiguration configuration,
         AuthenticationDataHolder authDataHolder )
         : base( configuration, authDataHolder )
      {
         this._headerName = configuration.HeaderName ?? HTTPHeaderBasedAuthenticationConfiguration.DEFAULT_HEADER_NAME;
      }

      public override Boolean CanBeUsed( HttpRequest matchContext, Boolean isAuthenticationAttempt )
      {
         return matchContext.Headers.ContainsKey( this._headerName );
      }

      protected override String GetAuthID( HttpRequest request )
      {
         return request.Headers[this._headerName];
      }

      protected override ValueTask<Boolean> SetAuthID( HttpResponse response, String authID )
      {
         response.Headers[this._headerName] = authID;
         // return true to signal that response can be sent
         return new ValueTask<Boolean>( true );
      }
   }


   public class HTTPHeaderBasedAuthenticationConfiguration : HTTPAuthenticatorConfiguration
   {
      public const String DEFAULT_HEADER_NAME = "X-MyAuthToken";
      public String HeaderName { get; set; } = DEFAULT_HEADER_NAME;
   }

}