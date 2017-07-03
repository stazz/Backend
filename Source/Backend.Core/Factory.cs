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
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Backend.Core
{
   public interface ResponseCreatorFactory<TMatchContext, TAuthenticatorMatchContext, TProcessContext, in TCreationParameters>
   {
      ValueTask<(ResponseCreatorMatcher<TMatchContext>, ResponseCreator<TAuthenticatorMatchContext, TProcessContext>)> CreateResponseCreatorAsync(
         TCreationParameters creationParameters,
         CancellationToken token
         );
   }

   public abstract class RegexBasedResponseCreatorFactory<TMatchContext, TAuthenticatorMatchContext, TProcessContext, TCreationParameters> : RegexHolder<TMatchContext>, ResponseCreatorFactory<TMatchContext, TAuthenticatorMatchContext, TProcessContext, TCreationParameters>
   {
      public RegexBasedResponseCreatorFactory(
         Func<TMatchContext, String> extractRegex,
         Regex regex
         ) : base( extractRegex, regex )
      {

      }

      public async ValueTask<(ResponseCreatorMatcher<TMatchContext>, ResponseCreator<TAuthenticatorMatchContext, TProcessContext>)> CreateResponseCreatorAsync(
         TCreationParameters creationParameters,
         CancellationToken token
         )
      {
         var matcher = new RegexBasedMatcher<TMatchContext>( this._extractRegex, this._regex );
         return (matcher, await this.DoCreateResponseCreatorAsync( creationParameters, token ));
      }

      protected abstract ValueTask<ResponseCreator<TAuthenticatorMatchContext, TProcessContext>> DoCreateResponseCreatorAsync(
         TCreationParameters creationParameters,
         CancellationToken token
         );
   }

   public interface AuthenticatorFactory<TContext, TMatchContext, in TCreationParameters>
   {
      ValueTask<Authenticator<TContext, TMatchContext>> CreateAuthenticatorAsync(
         TCreationParameters creationParameters,
         CancellationToken token
         );
   }

   public abstract class AuthenticatorFactoryImpl<TContext, TMatchContext, TCreationParameters> : AuthenticatorFactory<TContext, TMatchContext, TCreationParameters>
   {
      public abstract ValueTask<Authenticator<TContext, TMatchContext>> CreateAuthenticatorAsync(
         TCreationParameters creationParameters,
         CancellationToken token
         );
   }
}
