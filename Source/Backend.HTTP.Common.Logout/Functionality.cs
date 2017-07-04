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
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UtilPack;
using UtilPack.Configuration;

namespace Backend.HTTP.Server.Logout
{
   [ConfigurationType( typeof( LogoutConfiguration ) )]
   public class LogoutFunctionalityFactory : PathBasedRegexMatchingResponseCreatorFactory
   {
      private readonly LogoutResponseCreator _creator;

      public LogoutFunctionalityFactory(
         LogoutConfiguration logoutConfiguration
         ) : base( logoutConfiguration )
      {
         this._creator = new LogoutResponseCreator();
      }

      protected override ValueTask<ResponseCreator<HttpRequest, HttpContext>> DoCreateResponseCreatorAsync(
         ResponseCreatorInstantiationParameters creationParameters,
         CancellationToken token
         )
      {
         return new ValueTask<ResponseCreator<HttpRequest, HttpContext>>( this._creator );
      }
   }

   public class LogoutConfiguration : AbstractPathBasedConfiguration
   {

   }

   public class LogoutResponseCreator : ResponseCreator<HttpRequest, HttpContext>
   {
      public Task ProcessForResponseAsync(
         Object matchResult,
         HttpContext context,
         AuthenticatorAggregator<HttpRequest, HttpContext> authenticators
         )
      {
         foreach ( var authenticator in authenticators.AuthenticationSchemas.SelectMany( schema => authenticators.GetAuthenticators( schema ) ?? Empty<Authenticator<HttpContext>>.Enumerable ) )
         {
            authenticator.UnregisterUser( context );
         }

         return null;
      }
   }
}
