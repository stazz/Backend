/*
 * Copyright 2018 Stanislav Muhametsin. All rights Reserved.
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
using Microsoft.AspNetCore.Http;
using System;
using System.Threading;
using System.Threading.Tasks;
using UtilPack.Configuration;

namespace Backend.HTTP.Common.GuestAuthenticator
{

   public sealed class GuestHTTPAuthenticator : HTTPAuthenticator
   {
      private readonly String _userID;

      private readonly String _authID;

      public GuestHTTPAuthenticator(
         GuestHTTPAuthenticatorConfiguration configuration,
         AuthenticationDataHolder authDataHolder
         ) : base( ProcessConfiguration( configuration ), authDataHolder )
      {
         this._userID = configuration.UserID;
         // Calling virtual method in constructor - but it's ok, since this is sealed. But really need to make better solution (remove virtuality, or make static method, or ...).
         this._authID = this.GenerateAuthID( this._userID );
         authDataHolder.AddAuthData( this._authID, this._userID, Timeout.InfiniteTimeSpan );
      }

      public override Boolean CanBeUsed(
         HttpRequest matchContext,
         Boolean isAuthenticationAttempt
         )
      {
         return true;
      }

      protected override String GetAuthID(
         HttpRequest request
         )
      {
         return this._authID;
      }

      protected override ValueTask<Boolean> SetAuthID(
         HttpResponse response,
         String authID
         )
      {
         // return true to signal that response can be sent
         return new ValueTask<Boolean>( true );
      }

      private static GuestHTTPAuthenticatorConfiguration ProcessConfiguration( GuestHTTPAuthenticatorConfiguration config )
      {
         config.AuthenticationTokenExpirationTime = Timeout.InfiniteTimeSpan;
         return config;
      }
   }


   public class GuestHTTPAuthenticatorConfiguration : HTTPAuthenticatorConfiguration
   {
      public String UserID { get; set; } = "guest";
   }

   [ConfigurationType( typeof( GuestHTTPAuthenticatorConfiguration ) )]
   public class GuestHTTPAuthenticatorFactory : AuthenticatorFactoryImpl
   {
      private readonly GuestHTTPAuthenticatorConfiguration _config;

      public GuestHTTPAuthenticatorFactory( GuestHTTPAuthenticatorConfiguration config )
      {
         this._config = config;
      }

      public override ValueTask<Authenticator<HttpContext, HttpRequest>> CreateAuthenticatorAsync(
         AuthenticationDataHolder creationParameters,
         CancellationToken token
         )
      {
         return new ValueTask<Authenticator<HttpContext, HttpRequest>>( new GuestHTTPAuthenticator( this._config, creationParameters ) );
      }
   }
}
