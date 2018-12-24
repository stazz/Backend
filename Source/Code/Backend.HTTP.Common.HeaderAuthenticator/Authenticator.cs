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

namespace Backend.HTTP.Common.HeaderAuthenticator
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