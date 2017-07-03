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
using System.Threading.Tasks;
using Backend.Core;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using UtilPack;
using UtilPack.Cryptography.Digest;
using UtilPack.Cryptography;

namespace Backend.HTTP.Common
{
   public abstract class HTTPAuthenticator : Authenticator<HttpContext, HttpRequest>
   {
      private readonly Int32 _authIDByteCount;
      private readonly Char[] _base64Encode;
      private readonly TimeSpan _expirationTime;
      private readonly AuthenticationDataHolder _authData;

      public HTTPAuthenticator(
         HTTPAuthenticatorConfiguration configuration,
         AuthenticationDataHolder authDataHolder
         )
      {
         this._authData = ArgumentValidator.ValidateNotNull( nameof( authDataHolder ), authDataHolder );

         this._authIDByteCount = Math.Max( 1, configuration.AuthenticationTokenByteCount );
         this._expirationTime = configuration.AuthenticationTokenExpirationTime;
         var chars = StringConversions.CreateBase64EncodeLookupTable( true );
         using ( var rng = new DigestBasedRandomGenerator( new SHA512() ) )
         {
            rng.AddSeedMaterial( configuration.AuthenticationTokenBase64ShuffleSeed );
            using ( var secRandom = new SecureRandom( rng ) )
            {
               chars.Shuffle( secRandom );
            }
         }

         this._base64Encode = chars;
      }

      public abstract Boolean CanBeUsed( HttpRequest matchContext, Boolean isAuthenticationAttempt );

      public ValueTask<ChallengeResult> ChallengeAsync(
         HttpContext context
         )
      {
         var authID = this.GetAuthID( context.Request );
         UserInfo userInfo;
         if ( this._authData.TryGetAuthData( authID, out var authIDInfo, out var authUserInfo ) )
         {
            userInfo = authUserInfo.UserInfo;
         }
         else
         {
            this._authData.RemoveAuthData( authID );
            userInfo = null;
         }

         return new ValueTask<ChallengeResult>( new ChallengeResult(
            userInfo,
            userInfo != null ? this.MarkChallengeAccepted( context.Response, authID ) : this.GiveUpOnChallenge( context.Response )
            ) );
      }

      public async Task RegisterUser( HttpContext context, String userID )
      {
         var authID = this.GenerateAuthID( userID );
         this._authData.AddAuthData( authID, userID, this._expirationTime );
         await this.MarkChallengeAccepted( context.Response, authID )();
      }

      public void UnregisterUser( HttpContext context )
      {
         var authID = this.GetAuthID( context.Request );
         if ( !authID.IsNullOrEmpty() )
         {
            this._authData.RemoveAuthData( authID );
         }
      }

      protected abstract String GetAuthID( HttpRequest request );
      protected abstract ValueTask<Boolean> SetAuthID( HttpResponse response, String authID );

      protected virtual String GenerateAuthID( String userID )
      {
         using ( var rng = DigestBasedRandomGenerator.CreateAndSeedWithDefaultLogic( new SHA512() ) )
         {
            var bytez = new Byte[this._authIDByteCount];
            rng.NextBytes( bytez );
            return StringConversions.EncodeBinary( bytez, this._base64Encode );
         }
      }

      private Func<Task> GiveUpOnChallenge( HttpResponse response )
      {
         return async () =>
         {
            await HTTPAuthenticatorAggregator.PerformProceedWhenNoAuthenticatorFound( response );
         };
      }

      private Func<Task> MarkChallengeAccepted( HttpResponse response, String authID )
      {
         return async () =>
         {
            if ( await this.SetAuthID( response, authID ) )
            {
               await response.WriteAsync( "" );
            }
         };
      }
   }

   public class HTTPAuthenticatorConfiguration
   {
      public Int64 AuthenticationTokenBase64ShuffleSeed { get; set; }
      public Int32 AuthenticationTokenByteCount { get; set; } = 32;
      public TimeSpan AuthenticationTokenExpirationTime { get; set; } = TimeSpan.FromHours( 3 ); // 3 hours
   }



}
