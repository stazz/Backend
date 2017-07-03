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
using System.Threading.Tasks;

namespace Backend.Core
{
   public interface ResponseCreator<out TAuthenticatorMatchContext, TProcessContext>
   {
      Task ProcessForResponseAsync(
         Object matchResult,
         TProcessContext context,
         AuthenticatorAggregator<TAuthenticatorMatchContext, TProcessContext> authenticators
      );
   }

   public interface ResponseCreatorMatcher<in TContext>
   {
      Object IsMatch( TContext request, out Boolean wasMatch );
   }

   public class RegexHolder<TContext>
   {
      protected readonly Func<TContext, String> _extractRegex;
      protected readonly Regex _regex;

      public RegexHolder(
         Func<TContext, String> extractRegex,
         Regex regex
         )
      {
         this._extractRegex = extractRegex ?? throw new ArgumentNullException( nameof( extractRegex ) );
         this._regex = regex ?? throw new ArgumentNullException( nameof( regex ) );
      }
   }

   public class RegexBasedMatcher<TContext> : RegexHolder<TContext>, ResponseCreatorMatcher<TContext>
   {

      public RegexBasedMatcher(
         Func<TContext, String> extractRegex,
         Regex regex
         ) : base( extractRegex, regex )
      {
      }

      public Object IsMatch( TContext request, out Boolean wasMatch )
      {
         var retVal = this._regex.Match( this._extractRegex( request ) );
         wasMatch = retVal.Success;
         return retVal;
      }
   }

   public abstract class AuthenticationGuardedResponseCreator<TAuthenticatorMatchContext, TProcessContext> : ResponseCreator<TAuthenticatorMatchContext, TProcessContext>
   {

      protected AuthenticationGuardedResponseCreator( String authenticationSchema )
      {
         this.AuthenticationSchema = authenticationSchema;
      }

      public async Task ProcessForResponseAsync(
         Object matchResult,
         TProcessContext context,
         AuthenticatorAggregator<TAuthenticatorMatchContext, TProcessContext> authenticators
         )
      {
         var authenticator = authenticators
            .GetAuthenticator( this.GetAuthenticatorMatchContext( context ), this.AuthenticationSchema );
         if ( authenticator != null )
         {
            var challengeResult = await authenticator.ChallengeAsync( context );
            if ( challengeResult.IsSuccess() )
            {
               await this.ProcessForResponseAsync( matchResult, context, challengeResult.UserInfo );
            }
            else
            {
               await challengeResult.Proceed();
            }
         }
         else
         {
            await authenticators.ProceedWhenNoAuthenticatorFound( context );
         }
      }

      protected String AuthenticationSchema { get; }

      protected abstract Task ProcessForResponseAsync( Object matchResult, TProcessContext context, UserInfo userInfo );

      protected abstract TAuthenticatorMatchContext GetAuthenticatorMatchContext( TProcessContext context );
   }

   public abstract class PublicResponseCreator<TAuthenticatorMatchContext, TProcessContext> : ResponseCreator<TAuthenticatorMatchContext, TProcessContext>
   {
      public Task ProcessForResponseAsync(
         Object matchResult,
         TProcessContext context,
         AuthenticatorAggregator<TAuthenticatorMatchContext, TProcessContext> authenticators
         )
      {
         return this.ProcessForResponseAsync( matchResult, context );
      }

      protected abstract Task ProcessForResponseAsync( Object matchResult, TProcessContext context );
   }
}
