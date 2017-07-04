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
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace Backend.Core
{
   public interface AuthenticatorAggregator
   {
      IEnumerable<String> AuthenticationSchemas { get; }
   }

   public interface AuthenticatorAggregator<in TMatchContext, in TContext> : AuthenticatorAggregator
   {
      Authenticator<TContext> GetAuthenticator( TMatchContext searchContext, String schema, Boolean isAuthenticationAttempt = false );
      IEnumerable<Authenticator<TContext>> GetAuthenticators( String schema );

      Task ProceedWhenNoAuthenticatorFound( TContext context );
   }

   public interface Authenticator<in TContext>
   {
      ValueTask<ChallengeResult> ChallengeAsync( TContext context );

      Task RegisterUser( TContext context, String userID );

      void UnregisterUser( TContext context );
   }

   public interface Authenticator<in TContext, in TMatchContext> : Authenticator<TContext>
   {
      Boolean CanBeUsed( TMatchContext matchContext, Boolean isAuthenticationAttempt );
   }

   public struct ChallengeResult
   {

      public ChallengeResult(
         UserInfo userInfo,
         Func<Task> proceed
         )
      {
         this.UserInfo = userInfo;
         this.Proceed = proceed;
      }

      public UserInfo UserInfo { get; }

      public Func<Task> Proceed { get; }

   }

   public interface UserInfo : IDisposable
   {
      String ID { get; }

      T GetOrAddUserData<T>( String key, Func<String, T> factory );
   }

   public abstract class DefaultAuthenticatorAggregator<TMatchContext, TContext> : AuthenticatorAggregator<TMatchContext, TContext>
   {
      private readonly IDictionary<String, Authenticator<TContext, TMatchContext>[]> _checkers;

      public DefaultAuthenticatorAggregator(
         IDictionary<String, Authenticator<TContext, TMatchContext>[]> checkers = null
         )
      {
         this._checkers = checkers ?? new Dictionary<String, Authenticator<TContext, TMatchContext>[]>();
      }

      public Authenticator<TContext> GetAuthenticator( TMatchContext matchContext, String schema, Boolean isAuthenticationAttempt )
      {
         this._checkers.TryGetValue( schema ?? "", out var authenticators );
         return authenticators?.FirstOrDefault( authenticator => authenticator.CanBeUsed( matchContext, isAuthenticationAttempt ) );
      }

      public IEnumerable<Authenticator<TContext>> GetAuthenticators( String schema )
      {
         this._checkers.TryGetValue( schema ?? "", out var retVal );
         return retVal?.Skip( 0 );
      }

      public IEnumerable<String> AuthenticationSchemas
      {
         get
         {
            return this._checkers.Keys;
         }
      }

      public abstract Task ProceedWhenNoAuthenticatorFound( TContext context );
   }


}

public static partial class E_Backend
{
   public static Boolean IsSuccess( this ChallengeResult challengeResult )
   {
      return challengeResult.UserInfo != null;
   }
}
